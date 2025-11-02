using UnityEngine;

namespace SkyNESemu
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
        
        public byte[] CHRROM = new byte[CHRROM_PLACEHOLDER_SIZE];
        public byte[] VRAM = new byte[VRAM_SIZE];
        public byte[] PaletteRAM = new byte[PALETTE_RAM_SIZE];

        private void Awake()
        {
            _cpu = GetComponent<CPU>();

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

                            Color color = new Color32((byte)(colorID * 85), (byte)(colorID * 85), (byte)(colorID * 85), 255);
                            _bgTexture.SetPixel(x + column * 8, y + row * 8, color);
                        }
                    }
                }
            }
            _bgTexture.Apply();
        }
    }
}