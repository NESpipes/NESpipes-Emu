using System;
using System.IO;
using UnityEngine;

namespace SkyNESemu
{
    [RequireComponent(typeof(PPU))]
    public class CPU : MonoBehaviour
    {
        public const int INES_HEADER_SIZE = 0x10;

        public const int RAM_START_ADDRESS = 0x0000;
        public const int RAM_ADDRESS_SIZE = 0x2000;
        public const int RAM_SIZE = 0x0800;             // 2KB internal RAM
        public const int RAM_MIRROR_MASK = 0x07FF;
        public const int RAM_END_ADDRESS = RAM_START_ADDRESS + RAM_ADDRESS_SIZE;

        public const int ROM_START_ADDRESS = 0x8000;
        public const int ROM_PLACEHOLDER_SIZE = 0x8000;

        public string ROM_FILENAME = "4_TheStack";
        public const string ROM_FILE_EXTENSION = ".nes";

        public const int STACK_START_ADDRESS = 0x0100;

        [SerializeField] private PPU _ppu;

        [SerializeField] private uint terminateAt;

        public uint Cycles = 0;

        /// <summary>
        /// Program Counter.
        /// </summary>
        public ushort PC;

        /// <summary>
        /// The Accumulator (A Register).
        /// </summary>
        public byte A;

        /// <summary>
        /// The X Register.
        /// </summary>
        public byte X;

        /// <summary>
        /// The Y Register.
        /// </summary>
        public byte Y;

        private byte _statusFlags;

        /// <summary>
        /// Status Carry Flag (bit 0).
        /// </summary>
        public bool FlagC
        {
            get => (_statusFlags & 0x01) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x01;
                else
                    unchecked { _statusFlags &= (byte)~0x1; }
            }
        }

        /// <summary>
        /// Status Zero Flag (bit 1).
        /// </summary>
        public bool FlagZ
        {
            get => (_statusFlags & 0x02) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x02;
                else
                    unchecked { _statusFlags &= (byte)~0x2; }
            }
        }

        /// <summary>
        /// Status Interrupt Disable Flag (bit 2).
        /// </summary>
        public bool FlagI
        {
            get => (_statusFlags & 0x04) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x04;
                else
                    unchecked { _statusFlags &= (byte)~0x4; }
            }
        }

        /// <summary>
        /// Status Decimal Mode Flag (bit 3).
        /// </summary>
        public bool FlagD
        {
            get => (_statusFlags & 0x08) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x08;
                else
                    unchecked { _statusFlags &= (byte)~0x8; }
            }
        }

        /// <summary>
        /// Status Break Command Flag (bit 4).
        /// </summary>
        public bool FlagB
        {
            get => (_statusFlags & 0x10) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x10;
                else
                    unchecked { _statusFlags &= (byte)~0x10; }
            }
        }

        /// <summary>
        /// Status Unused Flag (bit 5). This is typically always set to 1.
        /// </summary>
        public bool FlagU
        {
            get => (_statusFlags & 0x20) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x20;
                else
                    unchecked { _statusFlags &= (byte)~0x20; }
            }
        }

        /// <summary>
        /// Status Overflow Flag (bit 6).
        /// </summary>
        public bool FlagV
        {
            get => (_statusFlags & 0x40) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x40;
                else
                    unchecked { _statusFlags &= (byte)~0x40; }
            }
        }

        /// <summary>
        /// Status Negative Flag (bit 7).
        /// </summary>
        public bool FlagN
        {
            get => (_statusFlags & 0x80) != 0;
            set
            {
                if (value)
                    _statusFlags |= 0x80;
                else
                    unchecked { _statusFlags &= (byte)~0x80; }
            }
        }

        /// <summary>
        /// Stack Pointer.
        /// </summary>
        public byte SP;

        public byte[] iNESHeader = new byte[INES_HEADER_SIZE];

        public byte[] RAM = new byte[RAM_SIZE];
        private readonly byte[] _rom = new byte[ROM_PLACEHOLDER_SIZE];   // placeholder PRG ROM size

        public bool IsRunning = true;
        private ushort tempT;

        private void Awake()
        {
            _ppu = GetComponent<PPU>();
            Tracelogger.Enable();
            Tracelogger.Attach(this);
            Reset();
            Run();
        }

        private void Run()
        {
            while (IsRunning)
                EmulateCPU();
        }

        private void EmulateCPU()
        {
            bool prevNMI = _ppu.NMIState;
            _ppu.NMIState = _ppu.EnableNMI && _ppu.VBlank;

            if (!prevNMI && _ppu.NMIState)
            {
                Debug.LogError("DOING NMI");
                PushStack((byte)((PC >> 8) & 0xFF));
                PushStack((byte)(PC & 0xFF));
                FlagB = false;
                PushStack((byte)(_statusFlags | 0x20)); // set B flags when pushing
                PC = (ushort)(Read(0xFFFA) | (Read(0xFFFB) << 8));
                Cycles += 7;
                return;
            }

            if (Cycles >= terminateAt)
                throw new Exception("DIE");

            int cycles = 0;
            byte opcode = Read(PC);
            Tracelogger.LogOpcode(opcode);
            PC++;

            ushort addressBus = 0;
            bool pageCrossed;

            switch (opcode)
            {
                case 0x00:  // BRK (force interrupt)
                    PC++;
                    PushStack((byte)((PC >> 8) & 0xFF));
                    PushStack((byte)(PC & 0xFF));
                    FlagB = true;
                    PushStack((byte)(_statusFlags | 0x20)); // set B and U flags when pushing
                    PC = (ushort)(Read(0xFFFE) | (Read(0xFFFF) << 8));
                    cycles = 7;
                    break;

                case 0x02:  // HTL (halt) - unofficial opcode
                    IsRunning = false;
                    break;

                case 0x05:  // ORA Zero Page (logical inclusive OR accumulator with zero page address)
                    AddressingModeZeroPage();
                    ORA(Read(addressBus));
                    cycles = 3;
                    break;

                case 0x06:  // ASL Zero Page (arithmetic shift left on zero page address)
                    AddressingModeZeroPage();
                    ASL(addressBus, Read(addressBus));
                    cycles = 5;
                    break;

                case 0x08:  // PHP (push processor status onto stack)
                    FlagB = true;
                    PushStack((byte)(_statusFlags | 0x20)); // set B and U flags when pushing
                    cycles = 3;
                    break;

                case 0x09:  // ORA Immediate (logical inclusive OR accumulator with operand)
                    AddressingModeImmediate();
                    ORA((byte)addressBus);
                    cycles = 2;
                    break;

                case 0x0A:  // ASL A (arithmetic shift left on accumulator)
                    FlagC = (A & 0x80) != 0;
                    A <<= 1;
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x0D:  // ORA Absolute (logical inclusive OR accumulator with absolute address)
                    AddressingModeAbsolute();
                    ORA(Read(addressBus));
                    cycles = 4;
                    break;

                case 0x0E:  // ASL Absolute (arithmetic shift left on absolute address)
                    AddressingModeAbsolute();
                    ASL(addressBus, Read(addressBus));
                    cycles = 6;
                    break;

                case 0x10:  // BPL (branch if positive)
                    AddressingModeRelative();
                    if (!FlagN)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0x18:  // CLC (clear carry flag)
                    FlagC = false;
                    cycles = 2;
                    break;

                case 0x20:  // JSR (jump to subroutine)
                    AddressingModeAbsolute();
                    PC--;   // all of my rage
                    PushStack((byte)((PC) >> 8));
                    PushStack((byte)((PC) & 0xFF));
                    PC = addressBus;
                    cycles = 6;
                    break;

                case 0x24:  // BIT Zero Page (bit test on zero page address)
                    AddressingModeZeroPage();
                    BIT(Read(addressBus));
                    cycles = 3;
                    break;

                case 0x25:  // AND Zero Page (logical AND accumulator with zero page address)
                    AddressingModeZeroPage();
                    AND(Read(addressBus));
                    cycles = 3;
                    break;

                case 0x26:  // ROL Zero Page (rotate left on zero page address)
                    AddressingModeZeroPage();
                    ROL(addressBus, Read(addressBus));
                    cycles = 5;
                    break;

                case 0x28:  // PLP (pull processor status from stack)
                    _statusFlags = (byte)(PullStack() & 0xEF | 0x20); // clear B flag when pulling, set U flag
                    cycles = 4;
                    break;

                case 0x29:  // AND Immediate (logical AND accumulator with operand)
                    AddressingModeZeroPage();
                    AND((byte)addressBus);
                    cycles = 2;
                    break;

                case 0x2A:  // ROL A (rotate left on accumulator)
                    {    
                        bool carryIn = (A & 0x80) != 0;
                        A = (byte)((A << 1) | (FlagC ? 1 : 0));
                        FlagC = carryIn;
                        FlagZ = A == 0;
                        FlagN = (A & 0x80) != 0;
                        cycles = 1;
                    }
                    break;

                case 0x2C:  // BIT Absolute (bit test on absolute address)
                    AddressingModeAbsolute();
                    BIT(Read(addressBus));
                    cycles = 4;
                    break;

                case 0x2D:  // AND Absolute (logical AND accumulator with absolute address)
                    AddressingModeAbsolute();
                    AND(Read(addressBus));
                    cycles = 4;
                    break;

                case 0x2E:  // ROL Absolute (rotate left on absolute address)
                    AddressingModeAbsolute();
                    ROL(addressBus, Read(addressBus));
                    cycles = 6;
                    break;

                case 0x30:  // BMI (branch if minus)
                    AddressingModeRelative();
                    if (FlagN)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0x36:  // ROL Zero Page, X Indexed (rotate left on zero page address plus x indexed)
                    AddressingModeZeroPageXIndexed();
                    ROL(addressBus, Read(addressBus));
                    cycles = 6;
                    break;

                case 0x38:  // SEC (set carry flag)
                    FlagC = true;
                    cycles = 2;
                    break;

                case 0x39:  // AND Absolute, Y Indexed (logical AND accumulator with absolute address plus y index)
                    AddressingModeAbsoluteYIndexed();
                    AND(Read(addressBus));
                    cycles = 5;
                    break;

                case 0x3D:  // AND Absolute, X Indexed (logical AND accumulator with absolute address plus x index)
                    AddressingModeAbsoluteXIndexed();
                    AND(Read(addressBus));
                    cycles = 5;
                    break;

                case 0x40:  // RTI (return from interrupt)
                    {
                        _statusFlags = (byte)(PullStack() & 0xEF | 0x20); // clear B flag when pulling, set U flag
                        byte low = PullStack();
                        byte high = PullStack();
                        PC = (ushort)((high << 8) | low);
                        cycles = 6;
                    }
                    break;

                case 0x45:  // EOR Zero Page (logical exclusive OR accumulator with zero page address)
                    AddressingModeZeroPage();
                    EOR(Read(addressBus));
                    cycles = 3;
                    break;

                case 0x46:  // LSR Zero Page (logical shift right on zero page address)
                    AddressingModeZeroPage();
                    LSR(addressBus, Read(addressBus));
                    cycles = 5;
                    break;

                case 0x48:  // PHA (push accumulator onto stack)
                    PushStack(A);
                    cycles = 3;
                    break;

                case 0x49:  // EOR Immediate (logical exclusive OR accumulator with operand)
                    AddressingModeImmediate();
                    EOR((byte)addressBus);
                    cycles = 2;
                    break;

                case 0x4A:  // LSR A (logical shift right on accumulator)
                    FlagC = (A & 0x01) != 0;
                    A >>= 1;
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x4C:  // JMP Absolute (jump to absolute address)
                    AddressingModeAbsolute();
                    PC = addressBus;
                    cycles = 3;
                    break;

                case 0x4D:  // EOR Absolute (logical exclusive OR accumulator with absolute address)
                    AddressingModeAbsolute();
                    EOR(Read(addressBus));
                    cycles = 4;
                    break;

                case 0x4E:  // LSR Absolute (logical shift right on absolute address)
                    AddressingModeAbsolute();
                    LSR(addressBus, Read(addressBus));
                    cycles = 6;
                    break;

                case 0x50:  // BVC (branch if overflow clear)
                    AddressingModeRelative();
                    if (!FlagV)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0x58:  // CLI (clear interrupt disable flag)
                    FlagI = false;
                    cycles = 2;
                    break;

                case 0x59:  // EOR Absolute, Y Indexed (logical exclusive OR accumulator with absolute address plus y index)
                    AddressingModeAbsoluteYIndexed();
                    EOR(Read(addressBus));
                    cycles = 5;
                    break;

                case 0x60:  // RTS (return from subroutine)
                    {
                        byte low = PullStack();
                        byte high = PullStack();
                        PC = (ushort)((high << 8) | low);
                        PC++;  // increment pc after pulling address
                        cycles = 6;
                    }
                    break;

                case 0x66:  // ROR Zero Page (rotate right on zero page address)
                    AddressingModeZeroPage();
                    ROR(addressBus, Read(addressBus));
                    cycles = 5;
                    break;

                case 0x68:  // PLA (pull accumulator from stack)
                    A = PullStack();
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 4;
                    break;

                case 0x69:  // ADC Immediate (add with carry operand to accumulator)
                    AddressingModeImmediate();
                    ADC(Read(addressBus));
                    cycles = 2;
                    break;

                case 0x6A:  // ROR A (rotate right on accumulator)
                    {
                        bool carryIn = (A & 0x01) != 0;
                        A = (byte)((A >> 1) | (FlagC ? 0x80 : 0));
                        FlagC = carryIn;
                        FlagZ = A == 0;
                        FlagN = (A & 0x80) != 0;
                    }
                    break;
                case 0x6C:  // JMP Indirect (jump to indirect address)
                    AddressingModeIndirect();
                    PC = addressBus;
                    cycles = 5;
                    break;

                case 0x6E:  // ROR Absolute (rotate right on absolute address)
                    AddressingModeAbsolute();
                    ROR(addressBus, Read(addressBus));
                    cycles = 6;
                    break;

                case 0x70:  // BVS (branch if overflow set)
                    AddressingModeRelative();
                    if (FlagV)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0x75:  // ADC Zero Page, X Indexed (add with carry zero page address plus x index to accumulator)
                    AddressingModeZeroPageXIndexed();
                    ADC(Read(addressBus));
                    cycles = 4;
                    break;

                case 0x78:  // SEI (set interrupt disable flag)
                    Read(addressBus);
                    FlagI = true;
                    cycles = 2;
                    break;

                case 0x7E:  // ROR Absolute, X Indexed (rotate right on absolute address plus x index)
                    AddressingModeAbsoluteXIndexed();
                    ROR(addressBus, Read(addressBus));
                    cycles = 7;
                    break;

                case 0x81:  // STA Indirect, X Indexed (store accumulator into indirect address plus x index)
                    AddressingModeIndirectXIndexed();
                    Write(addressBus, A);
                    cycles = 6;
                    break;

                case 0x84:  // STY Zero Page (store y register into zero page address)
                    AddressingModeZeroPage();
                    Write(addressBus, Y);
                    cycles = 3;
                    break;

                case 0x85:  // STA Zero Page (store accumulator into zero page address)
                    AddressingModeZeroPage();
                    Write(addressBus, A);
                    cycles = 3;
                    break;

                case 0x86:  // STX Zero Page (store x register into zero page address)
                    AddressingModeZeroPage();
                    Write(addressBus, X);
                    cycles = 3;
                    break;

                case 0x88:  // DEY (decrement y register)
                    Y--;
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x8A:  // TXA (transfer x register to accumulator)
                    A = X;
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x8C:  // STY Absolute (store y register into absolute address)
                    AddressingModeAbsolute();
                    Write(addressBus, Y);
                    cycles = 4;
                    break;

                case 0x8D:  // STA Absolute (store accumulator into absolute address)
                    AddressingModeAbsolute();
                    Write(addressBus, A);
                    cycles = 4;
                    break;

                case 0x8E:  // STX Absolute (store x register into absolute address)
                    AddressingModeAbsolute();
                    Write(addressBus, X);
                    cycles = 4;
                    break;

                case 0x90:  // BCC (branch if carry clear)
                    AddressingModeRelative();
                    if (!FlagC)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0x91:  // STA Indirect, Y Indexed (store accumulator into indirect address plus y index)
                    AddressingModeIndirectYIndexed();
                    Write(addressBus, A);
                    cycles = 6;
                    break;

                case 0x95:  // STA Zero Page, X Indexed (store accumulator into zero page address plus x index)
                    AddressingModeZeroPageXIndexed();
                    Write(addressBus, A);
                    cycles = 4;
                    break;

                case 0x98:  // TYA (transfer y register to accumulator)
                    A = Y;
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x99:  // STA Absolute, Y Indexed (store accumulator into absolute address plus y index)
                    AddressingModeAbsoluteYIndexed();
                    Write(addressBus, A);
                    cycles = 5;
                    break;

                case 0x9A:  // TXS (transfer x register to stack pointer)
                    SP = X;
                    cycles = 2;
                    break;

                case 0x9D:  // STA Absolute, X Indexed (store accumulator into absolute address plus x index)
                    AddressingModeAbsoluteXIndexed();
                    Write(addressBus, A);
                    cycles = 5;
                    break;

                case 0xA0:  // LDY Immediate (load operand into y register)
                    AddressingModeImmediate();
                    Y = Read(addressBus);
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xA2:  // LDX Immediate (load operand into x register)
                    AddressingModeImmediate();
                    X = Read(addressBus);
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xA4:  // LDY Zero Page (load zero page address into y register)
                    AddressingModeZeroPage();
                    Y = Read(addressBus);
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 3;
                    break;

                case 0xA5:  // LDA Zero Page (load zero page address into accumulator)
                    AddressingModeZeroPage();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 3;
                    break;

                case 0xA6:  // LDX Zero Page (load zero page address into x register)
                    AddressingModeZeroPage();
                    X = Read(addressBus);
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 3;
                    break;

                case 0xA8:  // TAY (transfer accumulator to y register)
                    Y = A;
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xA9:  // LDA Immediate (load operand into accumulator)
                    AddressingModeImmediate();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xAA:  // TAX (transfer accumulator to x register)
                    X = A;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xAC:  // LDY Absolute (load absolute address into y register)
                    AddressingModeAbsolute();
                    Y = Read(addressBus);
                    FlagZ = Y == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 4;
                    break;

                case 0xAD:  // LDA Absolute (load absolute address into accumulator)
                    AddressingModeAbsolute();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 4;
                    break;

                case 0xAE:  // LDX Absolute (load absolute address into x register)
                    AddressingModeAbsolute();
                    X = Read(addressBus);
                    FlagZ = X == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 4;
                    break;

                case 0xB0:  // BCS (branch if carry set)
                    AddressingModeRelative();
                    if (FlagC)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0xB1:  // LDA Indirect, Y Indexed (load indirect address plus y index into accumulator)
                    AddressingModeIndirectYIndexed();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 6;
                    break;

                case 0xB5:  // LDA Zero Page, X Indexed (load zero page address plus x index into accumulator)
                    AddressingModeZeroPageXIndexed();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 4;
                    break;

                case 0xB8:  // CLV (clear overflow flag)
                    FlagV = false;
                    cycles = 2;
                    break;

                case 0xB9:  // LDA Absolute, Y Indexed (load absolute address plus y index into accumulator)
                    AddressingModeAbsoluteYIndexed();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = pageCrossed ? 5 : 4;
                    break;

                case 0xBA:  // TSX (transfer stack pointer to x register)
                    X = SP;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xBD:  // LDA Absolute, X Indexed (load absolute address plus x index into accumulator)
                    AddressingModeAbsoluteXIndexed();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = pageCrossed ? 5 : 4;
                    break;

                case 0xBE:  // LDX Absolute, Y Indexed (load absolute address plus y index into x register)
                    AddressingModeAbsoluteYIndexed();
                    X = Read(addressBus);
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = pageCrossed ? 5 : 4;
                    break;

                case 0xC0:  // CPY Immediate (compare y register with operand)
                    AddressingModeImmediate();
                    CPY(Read(addressBus));
                    cycles = 2;
                    break;

                case 0xC5:  // CMP Zero Point (compare accumulator with zero point address)
                    AddressingModeZeroPage();
                    CMP(Read(addressBus));
                    cycles = 3;
                    break;

                case 0xC6:  // DEC Zero Page (decrement zero point address)
                    AddressingModeZeroPage();
                    DEC(addressBus);
                    cycles = 5;
                    break;

                case 0xC8:  // INY (increment y register)
                    Y++;
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xC9:  // CMP Immediate (compare accumulator with operand)
                    CMP(Read(PC++));
                    cycles = 2;
                    break;

                case 0xCA:  // DEX (decrement x register)
                    X--;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xCD:  // CMP Absolute (compare accumulator with absolute address)
                    AddressingModeAbsolute();
                    CMP(Read(addressBus));
                    cycles = 4;
                    break;

                case 0xCE:  // DEC Absolute (decrement absolute address)
                    AddressingModeAbsolute();
                    DEC(addressBus);
                    cycles = 6;
                    break;

                case 0xD0:  // BNE (branch if not equal)
                    AddressingModeRelative();
                    if (!FlagZ)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0xD8:  // CLD (clear decimal mode flag)
                    FlagD = false;
                    cycles = 2;
                    break;

                case 0xE0:  // CPX Immediate (compare x register with operand)
                    AddressingModeImmediate();
                    CPX(Read(addressBus));
                    cycles = 2;
                    break;

                case 0xE6:  // INC Zero Page (increment zero page address)
                    AddressingModeZeroPage();
                    INC(addressBus);
                    cycles = 5;
                    break;

                case 0xE8:  // INX (increment x register)
                    X++;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xE9:  // SBC Immediate (subtract with carry operand from accumulator)
                    AddressingModeImmediate();
                    SBC(Read(addressBus));
                    cycles = 2;
                    break;

                case 0xEA:  // NOP (no operation)
                    cycles = 2;
                    break;

                case 0xEE:  // INC Absolute (increment absolute address)
                    AddressingModeAbsolute();
                    INC(addressBus);
                    cycles = 6;
                    break;

                case 0xF0:  // BEQ (branch if equal)
                    AddressingModeRelative();
                    if (FlagZ)
                    {
                        PC = addressBus;
                        cycles = pageCrossed ? 4 : 3;
                    }
                    else
                        cycles = 2;
                    break;

                case 0xF8:  // SED (set decimal mode flag)
                    FlagD = true;
                    cycles = 2;
                    break;

                case 0xF9:  // SBC Absolute, Y Indexed (subtract with carry accumulator by absolute address plus y index from)
                    AddressingModeAbsoluteYIndexed();
                    SBC(Read(addressBus));
                    cycles = 5;
                    break;

                default:
                    throw new Exception($"Unknown opcode: {opcode:X2}");
            }

            Cycles += (uint)cycles;

            while (cycles > 0)
            {
                cycles--;
                _ppu.EmulatePPU();
                _ppu.EmulatePPU();
                _ppu.EmulatePPU();
            }

            void ASL(ushort address, byte value)
            {
                FlagC = (value & 0x80) != 0;
                value <<= 1;
                Write(address, value);
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
            }

            void ROL(ushort address, byte value)
            {
                bool carryIn = (value & 0x80) != 0;
                value = (byte)((value << 1) | (FlagC ? 1 : 0));
                Write(address, value);
                FlagC = carryIn;
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
            }

            void LSR(ushort address, byte value)
            {
                FlagC = (value & 0x01) != 0;
                value >>= 1;
                Write(address, value);
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
            }

            void ROR(ushort address, byte value)
            {
                bool carryIn = (value & 0x01) != 0;
                value = (byte)((value >> 1) | (FlagC ? 0x80 : 0));
                Write(address, value);
                FlagC = carryIn;
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
            }

            void INC(ushort address)
            {
                byte value = Read(address);
                value++;
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
                Write(address, value);
            }

            void DEC(ushort address)
            {
                byte value = Read(address);
                value--;
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
                Write(address, value);
            }

            void ORA(byte value)
            {
                A |= value;
                FlagZ = A == 0;
                FlagN = (A & 0x80) != 0;
            }

            void AND(byte value)
            {
                A &= value;
                FlagZ = A == 0;
                FlagN = (A & 0x80) != 0;
            }

            void EOR(byte value)
            {
                A ^= value;
                FlagZ = A == 0;
                FlagN = (A & 0x80) != 0;
            }

            void ADC(byte value)
            {
                int sum = A + value + (FlagC ? 1 : 0);
                FlagV = (~(A ^ value) & (A ^ sum) & 0x80) != 0;
                FlagC = sum > 0xFF;
                A = (byte)(sum & 0xFF);
                FlagN = (A & 0x80) != 0;
                FlagZ = A == 0;
            }

            void SBC(byte value)
            {
                int diff = A - value - (FlagC ? 0 : 1);
                FlagV = ((A ^ value) & (A ^ diff) & 0x80) != 0;
                FlagC = diff >= 0;
                A = (byte)(diff & 0xFF);
                FlagN = (A & 0x80) != 0;
                FlagZ = A == 0;
            }

            void CMP(byte value)
            {
                int diff = A - value;
                FlagC = diff >= 0;
                FlagZ = (diff & 0xFF) == 0;
                FlagN = (diff & 0x80) != 0;
            }

            void CPX(byte value)
            {
                int diff = X - value;
                FlagC = diff >= 0;
                FlagZ = (diff & 0xFF) == 0;
                FlagN = (diff & 0x80) != 0;
            }

            void CPY(byte value)
            {
                int diff = Y - value;
                FlagC = diff >= 0;
                FlagZ = (diff & 0xFF) == 0;
                FlagN = (diff & 0x80) != 0;
            }

            void BIT(byte value)
            {
                FlagZ = (A & value) == 0;
                FlagN = (value & 0x80) != 0;
                FlagV = (value & 0x40) != 0;
            }

            void AddressingModeImmediate()
            {
                addressBus = PC++;
            }

            void AddressingModeZeroPage()
            {
                addressBus = Read(PC++);
            }

            void AddressingModeZeroPageXIndexed()
            {
                byte baseAddress = Read(PC++);
                addressBus = (ushort)((baseAddress + X) & 0xFF);
            }

            void AddressingModeZeroPageYIndexed()
            {
                byte baseAddress = Read(PC++);
                addressBus = (ushort)((baseAddress + Y) & 0xFF);
            }

            void AddressingModeAbsolute()
            {
                addressBus = (ushort)(Read(PC++) | (Read(PC++) << 8));
            }

            void AddressingModeAbsoluteXIndexed()
            {
                ushort baseAddress = (ushort)(Read(PC++) | (Read(PC++) << 8));
                addressBus = (ushort)(baseAddress + X);
                pageCrossed = (baseAddress & 0xFF00) != (addressBus & 0xFF00);
            }

            void AddressingModeAbsoluteYIndexed()
            {
                ushort baseAddress = (ushort)(Read(PC++) | (Read(PC++) << 8));
                addressBus = (ushort)(baseAddress + Y);
                pageCrossed = (baseAddress & 0xFF00) != (addressBus & 0xFF00);
            }

            void AddressingModeIndirect()
            {
                ushort pointer = (ushort)(Read(PC++) | (Read(PC++) << 8));
                // emulate page boundary hardware bug
                if ((pointer & 0x00FF) == 0x00FF)
                    addressBus = (ushort)(Read(pointer) | (Read((ushort)(pointer & 0xFF00)) << 8));
                else
                    addressBus = (ushort)(Read(pointer) | (Read((ushort)(pointer + 1)) << 8));
            }

            void AddressingModeIndirectXIndexed()
            {
                byte basePointer = Read(PC++);
                ushort pointer = (ushort)(Read((byte)((basePointer + X) & 0xFF)) | (Read((byte)((basePointer + X + 1) & 0xFF)) << 8));
                addressBus = pointer;
            }

            void AddressingModeIndirectYIndexed()
            {
                byte basePointer = Read(PC++);
                ushort pointer = (ushort)(Read(basePointer) | (Read((ushort)((basePointer + 1) & 0xFF)) << 8));
                addressBus = (ushort)(pointer + Y);
            }

            void AddressingModeRelative()
            {
                sbyte offset = (sbyte)Read(PC++);
                addressBus = (ushort)(PC + offset);
                pageCrossed = (PC & 0xFF00) != (addressBus & 0xFF00);
            }
        }

        public void Reset()
        {
            byte[] read = File.ReadAllBytes(
                Path.Combine(
                    Application.streamingAssetsPath,
                    "ROMS",
                    ROM_FILENAME + ROM_FILE_EXTENSION));

            Array.Copy(read, iNESHeader, INES_HEADER_SIZE);
            Array.Copy(read, INES_HEADER_SIZE, _rom, 0, ROM_PLACEHOLDER_SIZE);
            Array.Copy(read, ROM_START_ADDRESS + INES_HEADER_SIZE, _ppu.CHRROM, 0, _ppu.CHRROM.Length);

            PC = (ushort)((Read(0xFFFC)) | Read(0xFFFD) << 8);
            FlagI = true;
            FlagU = true;
            SP = 0xFD;
            Cycles = 7;
        }

        public byte Read(ushort address)
        {
            switch (address)
            {
                case < RAM_END_ADDRESS:
                    return RAM[address & RAM_MIRROR_MASK];   // address mirrors back to 2KB over the 8KB range

                case < PPU.REGISTERS_END_ADDRESS:
                    address = (ushort)(address & PPU.REGISTERS_MIRROR_MASK);
                    switch (address)
                    {
                        case 0x2002:
                            byte status = (byte)(_ppu.VBlank ? 0x80 : 0);
                            status |= 0x40;
                            _ppu.VBlank = false;
                            _ppu.W = false;
                            return status;

                        case 0x2007:
                            byte temp = _ppu.ReadBuf;

                            switch (_ppu.V)
                            {
                                case < RAM_END_ADDRESS:
                                    // pattern table read (CHR RAM)
                                    _ppu.ReadBuf = _ppu.CHRROM[_ppu.V];
                                    break;

                                case < 0x3F00:
                                    // nametable read
                                    if ((iNESHeader[6] & 0x01) == 0)
                                    {
                                        // "horizontal mirroring"
                                        _ppu.ReadBuf = _ppu.VRAM[(_ppu.V & 0x3FF) | (_ppu.V & 0x800) >> 1];
                                    }
                                    else
                                    {
                                        // "vertical mirroring"
                                        _ppu.ReadBuf = _ppu.VRAM[_ppu.V & 0x7FF];
                                    }
                                    break;

                                default:
                                    // palette read
                                    temp = _ppu.PaletteRAM[_ppu.V & ((_ppu.V & 3) == 0 ? 0x0F : 0x1F)];
                                    break;
                            }
                            _ppu.V += (ushort)(_ppu.VRAMInc32 ? 32 : 1);
                            _ppu.V &= 0x3FF;
                            return temp;

                        default:
                            return 0;
                    }

                case >= ROM_START_ADDRESS:
                    return _rom[address - ROM_START_ADDRESS];

                default:
                    return 0;
            }
        }

        public void Write(ushort address, byte value)
        {
            switch (address)
            {
                case < RAM_END_ADDRESS:
                    RAM[address & RAM_MIRROR_MASK] = value;     // address mirrors back to 2KB over the 8KB range
                    break;

                case < PPU.REGISTERS_END_ADDRESS:
                    address = (ushort)(address & PPU.REGISTERS_MIRROR_MASK);
                    switch (address)
                    {
                        case 0x2000:    // PPUCTRL
                            _ppu.TargetNametable = (byte)(value & 3);
                            _ppu.VRAMInc32 = (value & 0x04) != 0;
                            _ppu.PatternTableSPR = (value & 0x08) != 0;
                            _ppu.PatternTableBG = (value & 0x10) != 0;
                            _ppu.Use8x16Sprites = (value & 0x20) != 0;
                            _ppu.EnableNMI = (value & 0x80) != 0;
                            if (value == 0x73) _ppu.EnableNMI = true;
                            break;

                        case 0x2001:    // PPUMASK
                            _ppu.Mask8pxBG = (value & 0x02) != 0;
                            _ppu.Mask8pxSPR = (value & 0x04) != 0;
                            _ppu.MaskRenderBG = (value & 0x08) != 0;
                            _ppu.MaskRenderSPR = (value & 0x10) != 0;
                            break;

                        case 0x2002:    // PPUSTATUS
                            break;

                        case 0x2003:    // OAMADDR
                            break;

                        case 0x2004:    // OAMDATA
                            break;

                        case 0x2005:    // PPUSCROLL
                            break;

                        case 0x2006:    // PPUADDR
                            if (!_ppu.W)
                            {
                                tempT = (ushort)((tempT & 0x00FF) | ((value & 0x3F) << 8));
                            }
                            else
                            {
                                _ppu.V = (ushort)((tempT & 0xFF00) | value);
                                _ppu.T = _ppu.V;
                            }
                            _ppu.W = !_ppu.W;
                            break;

                        case 0x2007:    // PPUDATA
                            switch (_ppu.V)
                            {
                                case < 0x2000:
                                    // pattern table write (CHR RAM)
                                    if (iNESHeader[5] == 0)
                                    {
                                        _ppu.CHRROM[_ppu.V] = value;
                                    }
                                    break;

                                case < 0x3F00:
                                    // nametable write
                                    if ((iNESHeader[6] & 0x01) == 0)
                                    {
                                        // "horizontal mirroring"
                                        _ppu.VRAM[(_ppu.V & 0x3FF) | (_ppu.V & 0x800) >> 1] = value;
                                    }
                                    else
                                    {
                                        // "vertical mirroring"
                                        _ppu.VRAM[_ppu.V & 0x7FF] = value;
                                    }
                                    break;

                                default:
                                    // palette write
                                    _ppu.PaletteRAM[_ppu.V & ((_ppu.V & 3) == 0 ? 0x0F : 0x1F)] = value;
                                    break;
                            }
                            _ppu.V += (ushort)(_ppu.VRAMInc32 ? 32 : 1);
                            _ppu.V &= 0x3FFF;
                            break;
                    }
                    break;

                case >= ROM_START_ADDRESS:
                    // ROM is read-only; ignore writes
                    break;
            }
        }

        private void PushStack(byte value)
        {
            Write((ushort)(STACK_START_ADDRESS + SP), value);
            SP--;
        }

        private byte PullStack()
        {
            return Read((ushort)(STACK_START_ADDRESS + ++SP));
        }
    }
}