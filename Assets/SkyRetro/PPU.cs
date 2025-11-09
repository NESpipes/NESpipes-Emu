using UnityEngine;

namespace SkyRetro
{
    [RequireComponent(typeof(CPU))]
    public class PPU : MonoBehaviour
    {
        public const int CHRROM_PLACEHOLDER_SIZE = 0x2000;

        public const int REGISTERS_START_ADDRESS = 0x2000;
        public const int REGISTERS_ADDRESS_SIZE = 0x2000;
        public const int REGISTERS_MIRROR_MASK = 0x2007;
        public const int REGISTERS_END_ADDRESS = REGISTERS_START_ADDRESS + REGISTERS_ADDRESS_SIZE;

        public const int VRAM_SIZE = 0x0800;
        public const int PALETTE_RAM_SIZE = 0x20;

        public Material BGMaterial;
        private Texture2D _bgTexture;

        [SerializeField] private CPU _cpu;

        /// <summary>
        /// Write Latch register.
        /// </summary>
        public bool W;

        /// <summary>
        /// Tranfer Address register.
        /// </summary>
        public ushort T;

        /// <summary>
        /// Current VRAM address register.
        /// </summary>
        public ushort V;

        public byte ReadBuf;

        public byte TargetNametable;
        public bool VRAMInc32 = false;
        public bool PatternTableSPR;
        public bool PatternTableBG;
        public bool Use8x16Sprites;
        public bool EnableNMI;

        public bool Mask8pxBG;
        public bool Mask8pxSPR;
        public bool MaskRenderBG;
        public bool MaskRenderSPR;

        public bool NMIState;

        public int Dot;
        public int Scanline;
        public bool VBlank = false;

        private readonly byte[] PALRGB = new byte[]
        {
            0x65, 0x65, 0x65, 0x00, 0x2A, 0x84, 0x15, 0x13, 0xA2, 0x3A, 0x01, 0x9E, 0x59, 0x00, 0x7A, 0x6A, 0x00, 0x3E, 0x68, 0x08, 0x00, 0x53, 0x1D, 0x00, 0x32, 0x34, 0x00, 0x0D, 0x46, 0x00, 0x00, 0x4F, 0x00, 0x00, 0x4C, 0x09, 0x00, 0x3F, 0x4B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xAE, 0xAE, 0xAE, 0x17, 0x5F, 0xD6, 0x43, 0x41, 0xFF, 0x75, 0x29, 0xFA, 0x9E, 0x1D, 0xCA, 0xB4, 0x20, 0x7B, 0xB1, 0x33, 0x22, 0x96, 0x4E, 0x00, 0x6A, 0x6C, 0x00, 0x39, 0x84, 0x00, 0x0F, 0x90, 0x00, 0x00, 0x8D, 0x33, 0x00, 0x7B, 0x8C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFE, 0xFE, 0xFE, 0x66, 0xAF, 0xFF, 0x93, 0x90, 0xFF, 0xC5, 0x78, 0xFF, 0xEE, 0x6C, 0xFF, 0xFF, 0x6F, 0xCA, 0xFF, 0x82, 0x71, 0xE6, 0x9E, 0x25, 0xBA, 0xBC, 0x00, 0x88, 0xD5, 0x01, 0x5E, 0xE1, 0x32, 0x47, 0xDD, 0x82, 0x4A, 0xCB, 0xDC, 0x4E, 0x4E, 0x4E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFE, 0xFE, 0xFE, 0xC0, 0xDE, 0xFF, 0xD2, 0xD1, 0xFF, 0xE7, 0xC7, 0xFF, 0xF8, 0xC2, 0xFF, 0xFF, 0xC3, 0xE9, 0xFF, 0xCB, 0xC4, 0xF5, 0xD7, 0xA5, 0xE2, 0xE3, 0x94, 0xCE, 0xED, 0x96, 0xBC, 0xF2, 0xAA, 0xB3, 0xF1, 0xCB, 0xB4, 0xE9, 0xF0, 0xB6, 0xB6, 0xB6, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        public Color32[] NESPALETTE = new Color32[64];
        
        public byte[] CHRROM = new byte[CHRROM_PLACEHOLDER_SIZE];
        public byte[] VRAM = new byte[VRAM_SIZE];
        public byte[] PaletteRAM = new byte[PALETTE_RAM_SIZE];

        private void Awake()
        {
            _cpu = GetComponent<CPU>();

            int i = 0;
            for (int j = 0; j < 64; j++)
                NESPALETTE[j] = new Color32(PALRGB[i++], PALRGB[i++], PALRGB[i++], 0xFF);

            _bgTexture = new Texture2D(32 * 8, 30 * 8, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            BGMaterial.mainTexture = _bgTexture;
        }

        private void Start()
        {
            Render();
        }

        public void EmulatePPU()
        {
            if (Dot == 1 && Scanline == 241)
                VBlank = true;
            else if (Dot == 1 && Scanline == 261)
                VBlank = false;

            Dot++;
            if (Dot > 341)
            {
                Dot = 0;
                Scanline++;
                if (Scanline > 261)
                    Scanline = 0;
            }
        }

        public void Render()
        {
            for (int row = 0; row < 30; row++)
            {
                for (int column = 0; column < 32; column++)
                {
                    for (int y = 0; y < 8; y++)
                    {
                        int offset = PatternTableBG ? 4096 : 0;
                        byte lb = CHRROM[VRAM[column + row * 32] * 16 + y + offset];
                        byte hb = CHRROM[VRAM[column + row * 32] * 16 + 8 + y + offset];

                        for (int x = 0; x < 8; x++)
                        {
                            int colorBit0 = (lb >> (7 - x)) & 1;
                            int colorBit1 = (hb >> (7 - x)) & 1;
                            int colorID = (colorBit1 << 1) | colorBit0;

                            Color32 color = NESPALETTE[PaletteRAM[colorID]];
                            _bgTexture.SetPixel(x + column * 8, y + row * 8, color);
                        }
                    }
                }
            }
            _bgTexture.Apply();
        }
    }
}