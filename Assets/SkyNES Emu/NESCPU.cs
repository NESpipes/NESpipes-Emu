using System;
using System.IO;
using UnityEngine;

namespace SkyNESEmu
{
    public class NESCPU : MonoBehaviour
    {
        public const int INES_HEADER_SIZE = 0x10;
        public const int RAM_SIZE = 0x0800;         // 2KB internal RAM
        public const int RAM_ACCESS_SIZE = 0x2000;

        public const int ROM_START_ADDRESS = 0x8000;
        public const int ROM_PLACEHOLDER_SIZE = 0x8000;

        public string ROM_FILENAME = "4_TheStack";
        public const string ROM_FILE_EXTENSION = ".nes";

        public const int STACK_START_ADDRESS = 0x0100;

        public uint Cycles = 0;

        /// <summary>
        /// Program Counter.
        /// </summary>
        public ushort PC;

        /// <summary>
        /// The A Register.
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

        public byte[] _ram = new byte[RAM_SIZE];
        private readonly byte[] _rom = new byte[ROM_PLACEHOLDER_SIZE];   // placeholder PRG ROM size

        private bool _isRunning = true;

        public void Awake()
        {
            Tracelogger.Enable();
            Tracelogger.Attach(this);
            Reset();
            Run();
        }

        private void Run()
        {
            while (_isRunning)
                EmulateCycle();
        }

        private void EmulateCycle()
        {
            byte opcode = Read(PC);
            Tracelogger.LogOpcode(opcode);
            PC++;

            int cycles = 0;
            ushort addressBus;
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
                    _isRunning = false;
                    break;

                case 0x05:  // ORA Zero Page (logical inclusive OR a register with zero page address)
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

                case 0x09:  // ORA Immediate (logical inclusive OR a register with operand)
                    AddressingModeImmediate();
                    ORA((byte)addressBus);
                    cycles = 2;
                    break;

                case 0x0A:  // ASL A (arithmetic shift left on a register)
                    FlagC = (A & 0x80) != 0;
                    A <<= 1;
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x0D:  // ORA Absolute (logical inclusive OR a register with absolute address)
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

                case 0x25:  // AND Zero Page (logical AND a register with zero page address)
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

                case 0x29:  // AND Immediate (logical AND a register with operand)
                    AddressingModeZeroPage();
                    AND((byte)addressBus);
                    cycles = 2;
                    break;

                case 0x2D:  // AND Absolute (logical AND a register with absolute address)
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

                case 0x38:  // SEC (set carry flag)
                    FlagC = true;
                    cycles = 2;
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

                case 0x45:  // EOR Zero Page (logical exclusive OR a register with zero page address)
                    AddressingModeZeroPage();
                    EOR(Read(addressBus));
                    cycles = 3;
                    break;

                case 0x46:  // LSR Zero Page (logical shift right on zero page address)
                    AddressingModeZeroPage();
                    LSR(addressBus, Read(addressBus));
                    cycles = 5;
                    break;

                case 0x48:  // PHA (push a register onto stack)
                    PushStack(A);
                    cycles = 3;
                    break;

                case 0x49:  // EOR Immediate (logical exclusive OR a register with operand)
                    AddressingModeImmediate();
                    EOR((byte)addressBus);
                    cycles = 2;
                    break;

                case 0x4A:  // LSR A (logical shift right on a register)
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

                case 0x4D:  // EOR Absolute (logical exclusive OR a register with absolute address)
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

                case 0x68:  // PLA (pull a register from stack)
                    A = PullStack();
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 4;
                    break;

                case 0x69:  // ADC Immediate (add with carry operand to a register)
                    AddressingModeImmediate();
                    ADC(Read(addressBus));
                    cycles = 2;
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

                case 0x75:  // ADC Zero Page, X Indexed (add with carry zero page address plus x index to a register)
                    AddressingModeZeroPageXIndexed();
                    ADC(Read(addressBus));
                    cycles = 4;
                    break;

                case 0x78:  // SEI (set interrupt disable flag)
                    FlagI = true;
                    cycles = 2;
                    break;

                case 0x81:  // STA Indirect, X Indexed (store a register into indirect address plus x index)
                    AddressingModeIndirectXIndexed();
                    Write(addressBus, A);
                    cycles = 6;
                    break;

                case 0x84:  // STY Zero Page (store y register into zero page address)
                    AddressingModeZeroPage();
                    Write(addressBus, Y);
                    cycles = 3;
                    break;

                case 0x85:  // STA Zero Page (store a register into zero page address)
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

                case 0x8A:  // TXA (transfer x register to a register)
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

                case 0x8D:  // STA Absolute (store a register into absolute address)
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

                case 0x91:  // STA Indirect, Y Indexed (store a register into indirect address plus y index)
                    AddressingModeIndirectYIndexed();
                    Write(addressBus, A);
                    cycles = 6;
                    break;

                case 0x95:  // STA Zero Page, X Indexed (store a register into zero page address plus x index)
                    AddressingModeZeroPageXIndexed();
                    Write(addressBus, A);
                    cycles = 4;
                    break;

                case 0x98:  // TYA (transfer y register to a register)
                    A = Y;
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0x9A:  // TXS (transfer x register to stack pointer)
                    SP = X;
                    cycles = 2;
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

                case 0xA5:  // LDA Zero Page (load zero page address into a register)
                    AddressingModeZeroPage();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 3;
                    break;

                case 0xA8:  // TAY (transfer a register to y register)
                    Y = A;
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xA9:  // LDA Immediate (load operand into a register)
                    AddressingModeImmediate();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xAA:  // TAX (transfer a register to x register)
                    X = A;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xAD:  // LDA Absolute (load absolute address into a register)
                    AddressingModeAbsolute();
                    A = Read(addressBus);
                    FlagZ = A == 0;
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

                case 0xB5:  // LDA Zero Page, X Indexed (load zero page address plus x index into a register)
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

                case 0xB9:  // LDA Absolute, Y Indexed (load absolute address plus y index into a register)
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

                case 0xBD:  // LDA Absolute, X Indexed (load absolute address plus x index into a register)
                    AddressingModeAbsoluteXIndexed();
                    A = Read(addressBus);
                    FlagZ = A == 0;
                    FlagN = (A & 0x80) != 0;
                    cycles = pageCrossed ? 5 : 4;
                    break;

                case 0xC0:  // CPY Immediate (compare y register with operand)
                    AddressingModeImmediate();
                    CPY(Read(addressBus));
                    cycles = 2;
                    break;

                case 0xC8:  // INY (increment y register)
                    Y++;
                    FlagZ = Y == 0;
                    FlagN = (Y & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xC9:  // CMP Immediate (compare a register with operand)
                    CMP(Read(PC++));
                    cycles = 2;
                    break;

                case 0xCA:  // DEX (decrement x register)
                    X--;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
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

                case 0xE8:  // INX (increment x register)
                    X++;
                    FlagZ = X == 0;
                    FlagN = (X & 0x80) != 0;
                    cycles = 2;
                    break;

                case 0xE9:  // SBC Immediate (subtract with carry operand from a register)
                    AddressingModeImmediate();
                    SBC(Read(addressBus));
                    cycles = 2;
                    break;

                case 0xEA:  // NOP (no operation)
                    cycles = 2;
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

                default:
                    throw new Exception($"Unknown opcode: {opcode:X2}");
            }

            Cycles += (uint)cycles;

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

            void INC(ushort address, byte value)
            {
                value++;
                Write(address, value);
                FlagZ = value == 0;
                FlagN = (value & 0x80) != 0;
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
                case < RAM_ACCESS_SIZE:
                    return _ram[address & 0x07FF];   // address mirrors back to 2KB over the 8KB range

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
                case < RAM_ACCESS_SIZE:
                    _ram[address & 0x07FF] = value;     // address mirrors back to 2KB over the 8KB range
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