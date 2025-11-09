using UnityEngine;

namespace SkyRetro
{
    public static class Tracelogger
    {
        public static CPU EmulatorCPU;
        public static bool Enabled = false;

        private static readonly string[] _opcodeNames = new string[256]
        {
            "BRK", "ORA", "HLT", "SLO", "NOP", "ORA", "ASL", "SLO",
            "PHP", "ORA", "ASL", "ANC", "NOP", "ORA", "ASL", "SLO",
            "BPL", "ORA", "HLT", "SLO", "NOP", "ORA", "ASL", "SLO",
            "CLC", "ORA", "NOP", "SLO", "NOP", "ORA", "ASL", "SLO",
            "JSR", "AND", "HLT", "RLA", "BIT", "AND", "ROL", "RLA",
            "PLP", "AND", "ROL", "ANC", "BIT", "AND", "ROL", "RLA",
            "BMI", "AND", "HLT", "RLA", "NOP", "AND", "ROL", "RLA",
            "SEC", "AND", "NOP", "RLA", "NOP", "AND", "ROL", "RLA",
            "RTI", "EOR", "HLT", "SRE", "NOP", "EOR", "LSR", "SRE",
            "PHA", "EOR", "LSR", "ASR", "JMP", "EOR", "LSR", "SRE",
            "BVC", "EOR", "HLT", "SRE", "NOP", "EOR", "LSR", "SRE",
            "CLI", "EOR", "NOP", "SRE", "NOP", "EOR", "LSR", "SRE",
            "RTS", "ADC", "HLT", "RRA", "NOP", "ADC", "ROR", "RRA",
            "PLA", "ADC", "ROR", "ARR", "JMP", "ADC", "ROR", "RRA",
            "BVS", "ADC", "HLT", "RRA", "NOP", "ADC", "ROR", "RRA",
            "SEI", "ADC", "NOP", "RRA", "NOP", "ADC", "ROR", "RRA",
            "NOP", "STA", "NOP", "SAX", "STY", "STA", "STX", "SAX",
            "DEY", "NOP", "TXA", "ANE", "STY", "STA", "STX", "SAX",
            "BCC", "STA", "HLT", "SHA", "STY", "STA", "STX", "SAX",
            "TYA", "STA", "TXS", "SHS", "SHY", "STA", "SHX", "SHA",
            "LDY", "LDA", "LDX", "LAX", "LDY", "LDA", "LDX", "LAX",
            "TAY", "LDA", "TAX", "LXA", "LDY", "LDA", "LDX", "LAX",
            "BCS", "LDA", "HLT", "LAX", "LDY", "LDA", "LDX", "LAX",
            "CLV", "LDA", "TSX", "LAS", "LDY", "LDA", "LDX", "LAX",
            "CPY", "CMP", "NOP", "DCP", "CPY", "CMP", "DEC", "DCP",
            "INY", "CMP", "DEX", "AXS", "CPY", "CMP", "DEC", "DCP",
            "BNE", "CMP", "HLT", "DCP", "NOP", "CMP", "DEC", "DCP",
            "CLD", "CMP", "NOP", "DCP", "NOP", "CMP", "DEC", "DCP",
            "CPX", "SBC", "NOP", "ISC", "CPX", "SBC", "INC", "ISC",
            "INX", "SBC", "NOP", "SBC", "CPX", "SBC", "INC", "ISC",
            "BEQ", "SBC", "HLT", "ISC", "NOP", "SBC", "INC", "ISC",
            "SED", "SBC", "NOP", "ISC", "NOP", "SBC", "INC", "ISC"
        };

        public static void Attach(CPU cpu)
        {
            EmulatorCPU = cpu;
        }

        public static void Detach()
        {
            EmulatorCPU = null;
        }

        public static void Enable()
        {
            Enabled = true;
        }

        public static void Disable()
        {
            Enabled = false;
        }

        public static void LogOpcode(byte opcode)
        {
            if (Enabled)
            {
                string line = string.Format(
                    "PC:{0:X4} {1:X2} {2} {3} A:{4:X2} X:{5:X2} Y:{6:X2} SP:{7:X2} {8} Cycles:{9}",
                    EmulatorCPU.PC,
                    opcode,
                    string.Join("-", EmulatorCPU.Read((ushort)(EmulatorCPU.PC + 1)).ToString("X2"), EmulatorCPU.Read((ushort)(EmulatorCPU.PC + 2)).ToString("X2")),
                    _opcodeNames[opcode],
                    EmulatorCPU.A,
                    EmulatorCPU.X,
                    EmulatorCPU.Y,
                    EmulatorCPU.SP,
                    string.Join(
                        "",
                        EmulatorCPU.FlagN ? "N" : "n",
                        EmulatorCPU.FlagV ? "V" : "v",
                        "-",
                        EmulatorCPU.FlagB ? "B" : "b",
                        EmulatorCPU.FlagD ? "D" : "d",
                        EmulatorCPU.FlagI ? "I" : "i",
                        EmulatorCPU.FlagZ ? "Z" : "z",
                        EmulatorCPU.FlagC ? "C" : "c"),
                    EmulatorCPU.Cycles);

                Debug.Log(line);
            }
        }
    }
}