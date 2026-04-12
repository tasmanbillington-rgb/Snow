using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using ClickableTransparentOverlay;
using ImGuiNET;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using Swed64;

namespace BasicESP
{
    public struct Rect {
        public Vector2 Min;
        public Vector2 Max;
        public Rect(Vector2 min, Vector2 max) { Min = min; Max = max; }
        public Rect(float x, float y, float w, float h) { Min = new Vector2(x, y); Max = new Vector2(x + w, y + h); }
        public bool Contains(Vector2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
    }

    public class ClickRipple
    {
        public Vector2 Position;
        public float Time;
        public float MaxRadius;
        public Vector4 Color;
    }

    public enum ESPPosition { 
        Left_Top, Left_Middle, Left_Bottom,
        Right_Top, Right_Middle, Right_Bottom,
        Top_Left, Top_Middle, Top_Right,
        Bottom_Left, Bottom_Middle, Bottom_Right
    }

    public class Renderer : Overlay
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        private ImFontPtr premiumFont;
        private bool fontsInitialized = false;

        private void EnsureFonts()
        {
            if (fontsInitialized) return;
            var io = ImGui.GetIO();
            var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
            if (!File.Exists(fontPath)) fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "verdana.ttf");
            
            if (File.Exists(fontPath))
            {
                // Note: On some versions of the library, adding font here might be too late.
                // We fallback to GetFont if this stays null or looks bad.
                try { premiumFont = io.Fonts.AddFontFromFileTTF(fontPath, 18.0f); } catch { }
            }
            fontsInitialized = true;
        }


        private bool showMenu = true;

        private bool wasInsertPressed = false;
        public Vector2 screenSize = new Vector2(1920, 1080);
        private ConcurrentQueue<Entity> entities = new ConcurrentQueue<Entity>();
        private Entity localPlayer = new Entity();
        private readonly object entityLock = new object();
        private bool enableESP = true;
        private bool showWatermark = true;
        private Vector4 enemyColor = new Vector4(1, 0, 0, 1);
        private Vector4 teamColor = new Vector4(0, 1, 0, 1);
        ImDrawListPtr drawList;

        public static bool enableSoftAim = false;
        public static float aimSmoothness = 5.0f;
        public static float aimFov = 100.0f;
        public static int softAimKey = 0x43; // Default to 'C'
        private bool showFovCircle = true;
        private Vector4 fovColor = new Vector4(1, 1, 1, 1);
        public static float fovOpacity = 1.0f;
        private bool showLines = true;
        private bool styleApplied = false;

        private int currentTab = 0;
        private string[] tabs = { "HOME", "VISUALS", "COLORS", "AIMBOT", "MISC", "KEYBINDS", "UPDATE" };
        private string[] tabIcons = { "[H]", "[V]", "[C]", "[A]", "[M]", "[K]", "[U]" }; 
        private string updateStatus = "Idle";
        private string updateDetail = "";
        private bool isUpdating = false;
        private Dictionary<string, float> toggleAnimations = new Dictionary<string, float>();
        private Dictionary<int, float> tabAnimations = new Dictionary<int, float>();

        private string userName = "User";
        private float[] usageData = new float[30];

        private string settingsPath = "settings.json";
        private string[] configPaths = { "config1.json", "config2.json", "config3.json" };
        private string[] configNames = { "Default", "Legit", "Rage" };
        private float homeFadeAlpha = 0f;
        private DateTime lastSaveTime = DateTime.Now;
        private float totalTimeMinutes = 0f;
        private DateTime lastUsageDate = DateTime.Now;
        private bool useSplitBox = false;
        private bool enableOutlines = true;

        private bool showSkeleton = false;
        private bool showBox = true;
        private Vector4 skeletonColor = new Vector4(1, 1, 1, 1);
        private bool useSkeletonGradient = false;
        private Vector4 skeletonColor2 = new Vector4(1, 1, 1, 1);
        
        private bool showBoxOutline = true;
        private bool showHealthOutline = true;
        private bool showSkeletonOutline = true;
        private bool showFovOutline = true;
        
        private int menuKey = 0x72;
        private int hideVisualsKey = 0xA4;
        private bool isVisualsHidden = false;
        private int remappingKeyIndex = -1;
        private bool wasHideKeyReleased = true;

        private bool menuSnowflakes = true;
        private bool espSnowflakes = true;
        private bool offsetsUpdated = false;
        private int espSnowflakeDensity = 200;
        private float espSnowflakeOpacity = 0.4f;
        private bool showHealthBar = true;
        private bool showDistance = true;
        private bool showHealthText = true;
        private bool showBombTimer = true;
        public static bool showTeam = false;
        public Swed swed;
        public static IntPtr bombPawn = IntPtr.Zero;
        
        private Vector4 healthBarColor = new Vector4(0, 1, 0, 1);
        private Vector4 boxColor = new Vector4(1, 0, 0, 1);
        private Vector4 lineColor = new Vector4(0, 1, 0, 1);
        private float boxOpacity = 1.0f;
        private float healthBarOpacity = 1.0f;
        private bool showHeadDot = false;
        private Vector4 headDotColor = new Vector4(1, 1, 1, 1);
        private float headDotOpacity = 1.0f;
        private bool enableGlow = true;
        private float glowIntensity = 3.0f;
        private bool showBoxGlow = true;
        private bool showSkeletonGlow = true;
        private bool showHealthGlow = true;
        private ESPPosition healthBarPos = ESPPosition.Left_Middle;
        private ESPPosition distancePos = ESPPosition.Bottom_Middle;
        private ESPPosition healthTextPos = ESPPosition.Left_Middle;
        private bool draggingHealthBar = false;
        private bool draggingDistance = false;
        private bool draggingHPText = false;
        private Vector2 dragHBOffset = Vector2.Zero;
        private Vector2 dragDistOffset = Vector2.Zero;
        private Vector2 dragHPTOffset = Vector2.Zero;
        private Vector4 accentColor = new Vector4(0.85f, 0.20f, 0.60f, 1.0f); // Pink Glow accent color

        private uint GetOutlineColor() => ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.8f));
        private bool isInteractingWithPreview = false;
        private Vector2 lastMenuPos = Vector2.Zero;
        private bool useAccentGradient = false;
        private Vector4 accentColor2 = new Vector4(1, 1, 1, 1);
        private bool useBoxGradient = false;
        private Vector4 boxColor2 = new Vector4(1, 1, 1, 1);
        private Vector4 teamBoxColor = new Vector4(0, 0.5f, 1, 1);
        private Vector4 teamBoxColor2 = new Vector4(0, 0.2f, 0.8f, 1);
        private bool useTeamBoxGradient = false;

        private Vector4 teamSkeletonColor = new Vector4(0.2f, 0.8f, 1, 1);
        private Vector4 teamSkeletonColor2 = new Vector4(0, 0.4f, 1, 1);
        private bool useTeamSkeletonGradient = false;

        private bool useHeadDotGradient = false;
        private Vector4 headDotColor2 = new Vector4(1, 1, 1, 1);

        private bool useHealthGradient = false;
        private Vector4 healthBarColor2 = new Vector4(1, 1, 1, 1);

        private bool useFovGradient = false;
        private Vector4 fovColor2 = new Vector4(1, 1, 1, 1);
        private List<ClickRipple> clickRipples = new List<ClickRipple>();

        public class ConfigData
        {
            public string UserName { get; set; } = "User";
            public float[] UsageData { get; set; } = new float[30];
            public string[] ConfigNames { get; set; } = { "Default", "Legit", "Rage" };
            public DateTime LastUsageDate { get; set; } = DateTime.Now;
            public float TotalTimeMinutes { get; set; } = 0f;
            
            public bool EnableESP { get; set; }
            public bool ShowBox { get; set; } = true;
            public bool ShowSkeleton { get; set; } = false;
            public float[] SkeletonColor { get; set; } = { 1, 1, 1, 1 };
            public bool UseSkeletonGradient { get; set; } = false;
            public float[] SkeletonColor2 { get; set; } = { 1, 1, 1, 1 };
            public bool ShowBoxOutline { get; set; } = true;
            public bool ShowHealthOutline { get; set; } = true;
            public bool ShowSkeletonOutline { get; set; } = true;
            public bool ShowFovOutline { get; set; } = true;
            public bool UseSplitBox { get; set; } = false;
            public bool EnableOutlines { get; set; } = true;
            public bool ShowLines { get; set; }
            public bool ShowHeadDot { get; set; }
            public bool ShowHealthBar { get; set; }
            public float[] BoxColor { get; set; } = new float[4];
            public bool UseBoxGradient { get; set; } = false;
            public float[] BoxColor2 { get; set; } = new float[4];
            public float BoxOpacity { get; set; }
            public float[] HeadDotColor { get; set; } = new float[4];
            public bool UseHeadDotGradient { get; set; } = false;
            public float[] HeadDotColor2 { get; set; } = new float[4];
            public float HeadDotOpacity { get; set; }
            public float[] HealthBarColor { get; set; } = new float[4];
            public bool UseHealthGradient { get; set; } = false;
            public float[] HealthBarColor2 { get; set; } = new float[4];
            public float HealthBarOpacity { get; set; }
            public float[] FovColor { get; set; } = new float[4];
            public bool UseFovGradient { get; set; } = false;
            public float[] FovColor2 { get; set; } = new float[4];
            public float FovOpacity { get; set; }
            public float[] AccentColor { get; set; } = new float[4];
            public bool UseAccentGradient { get; set; } = false;
            public float[] AccentColor2 { get; set; } = new float[4];

            public int MenuKey { get; set; } = 0x72;
            public int HideVisualsKey { get; set; } = 0xA4;

            public bool MenuSnowflakes { get; set; }
            public bool EspSnowflakes { get; set; }
            public int EspSnowflakeDensity { get; set; }
            public float EspSnowflakeOpacity { get; set; }
            public bool EnableGlow { get; set; }
            public float GlowIntensity { get; set; }
            public bool ShowBoxGlow { get; set; } = true;
            public bool ShowSkeletonGlow { get; set; } = true;
            public bool ShowHealthGlow { get; set; } = true;

            public bool ShowDistance { get; set; } = true;
            public bool ShowHealthText { get; set; } = true;
            public bool ShowBombTimer { get; set; } = true;
            public bool ShowTeam { get; set; } = false;

            public float[] TeamBoxColor { get; set; } = { 0, 0.5f, 1, 1 };
            public float[] TeamBoxColor2 { get; set; } = { 0, 0.2f, 0.8f, 1 };
            public bool UseTeamBoxGradient { get; set; } = false;
            public float[] TeamSkeletonColor { get; set; } = { 0.2f, 0.8f, 1, 1 };
            public float[] TeamSkeletonColor2 { get; set; } = { 0, 0.4f, 1, 1 };
            public bool UseTeamSkeletonGradient { get; set; } = false;
            
            public bool EnableSoftAim { get; set; }
            public float AimSmoothness { get; set; }
            public float AimFov { get; set; }
            public int SoftAimKey { get; set; } = 0x43;
            public bool ShowFovCircle { get; set; }
            public bool ShowWatermark { get; set; } = true;

            public int HealthBarPos { get; set; } = (int)ESPPosition.Left_Middle;
            public int DistancePos { get; set; } = (int)ESPPosition.Bottom_Middle;
            public int HealthTextPos { get; set; } = (int)ESPPosition.Left_Middle;
        }

        private class Snowflake
        {
            public Vector2 Position;
            public float Speed;
            public float Size;
        }
        private List<Snowflake> snowflakes = new List<Snowflake>();
        private List<Snowflake> overlaySnowflakes = new List<Snowflake>();
        private Random random = new Random();

        private void InitSnowflakes()
        {
            if (snowflakes.Count == 0)
            {
                for (int i = 0; i < 70; i++)
                {
                    snowflakes.Add(new Snowflake
                    {
                        Position = new Vector2((float)random.NextDouble() * 800, (float)random.NextDouble() * 600),
                        Speed = (float)random.NextDouble() * 30f + 10f,
                        Size = (float)random.NextDouble() * 1.5f + 1f
                    });
                }
            }
        }

        private void DrawSnowflakes(Vector2 windowPos, Vector2 windowSize)
        {
            var drawList = ImGui.GetWindowDrawList();
            float deltaTime = ImGui.GetIO().DeltaTime;
            
            foreach (var flake in snowflakes)
            {
                flake.Position.Y += flake.Speed * deltaTime;
                flake.Position.X += (float)Math.Sin(flake.Position.Y * 0.05f) * 0.2f;

                if (flake.Position.Y > windowSize.Y)
                {
                    flake.Position.Y = -5;
                    flake.Position.X = (float)random.NextDouble() * windowSize.X;
                }
                if (flake.Position.X > windowSize.X) flake.Position.X = 0;
                else if (flake.Position.X < 0) flake.Position.X = windowSize.X;

                drawList.AddCircleFilled(new Vector2(windowPos.X + flake.Position.X, windowPos.Y + flake.Position.Y), flake.Size, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.4f)));
            }
        }

        private void InitOverlaySnowflakes()
        {
            if (overlaySnowflakes.Count == 0)
            {
                UpdateOverlaySnowflakesList();
            }
        }

        private void UpdateOverlaySnowflakesList()
        {
            while (overlaySnowflakes.Count < espSnowflakeDensity)
            {
                overlaySnowflakes.Add(new Snowflake
                {
                    Position = new Vector2((float)random.NextDouble() * screenSize.X, (float)random.NextDouble() * screenSize.Y),
                    Speed = (float)random.NextDouble() * 30f + 10f,
                    Size = (float)random.NextDouble() * 1.5f + 1f
                });
            }
            while (overlaySnowflakes.Count > espSnowflakeDensity)
            {
                overlaySnowflakes.RemoveAt(overlaySnowflakes.Count - 1);
            }
        }

        private void UpdateOverlaySnowflakes()
        {
            UpdateOverlaySnowflakesList();
            float deltaTime = ImGui.GetIO().DeltaTime;
            foreach (var flake in overlaySnowflakes)
            {
                flake.Position.Y += flake.Speed * deltaTime;
                flake.Position.X += (float)Math.Sin(flake.Position.Y * 0.05f) * 0.2f;

                if (flake.Position.Y > screenSize.Y)
                {
                    flake.Position.Y = -5;
                    flake.Position.X = (float)random.NextDouble() * screenSize.X;
                }
                if (flake.Position.X > screenSize.X) flake.Position.X = 0;
                else if (flake.Position.X < 0) flake.Position.X = screenSize.X;
            }
        }

        private float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private void DrawGlowRect(Vector2 min, Vector2 max, Vector4 color, float intensity = 1.0f, float rounding = 0.0f, int layers = 12)
        {
            var drawList = ImGui.GetWindowDrawList();
            for (int i = 1; i <= layers; i++)
            {
                float alphaDecay = (float)Math.Pow(1.1f - (i / (float)layers), 3);
                uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, color.W * 0.20f * alphaDecay * intensity));
                float spread = i * 1.2f;
                drawList.AddRectFilled(min - new Vector2(spread, spread), max + new Vector2(spread, spread), gCol, rounding + spread);
            }
        }

        private void DrawGlowLine(Vector2 p1, Vector2 p2, Vector4 color, float intensity = 1.0f, int layers = 12)
        {
            var drawList = ImGui.GetWindowDrawList();
            for (int i = 1; i <= layers; i++)
            {
                float alphaDecay = (float)Math.Pow(1.1f - (i / (float)layers), 3);
                uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, color.W * 0.20f * alphaDecay * intensity));
                float spread = i * 1.2f;
                drawList.AddLine(p1, p2, gCol, 1.0f + spread);
            }
        }

        private void DrawGlowText(Vector2 pos, Vector4 color, string text, float intensity = 1.0f, int layers = 8)
        {
            var drawList = ImGui.GetWindowDrawList();
            uint mainCol = ImGui.ColorConvertFloat4ToU32(color);
            
            for (int i = 1; i <= layers; i++)
            {
                float alphaDecay = (float)Math.Pow(1.1f - (i / (float)layers), 2);
                uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, color.W * 0.18f * alphaDecay * intensity));
                float offset = i * 0.5f;
                
                drawList.AddText(pos + new Vector2(offset, 0), gCol, text);
                drawList.AddText(pos + new Vector2(-offset, 0), gCol, text);
                drawList.AddText(pos + new Vector2(0, offset), gCol, text);
                drawList.AddText(pos + new Vector2(0, -offset), gCol, text);
            }
            drawList.AddText(pos, mainCol, text);
        }

        private void DrawCursorGlow()
        {
            var drawList = ImGui.GetForegroundDrawList();
            var mousePos = ImGui.GetIO().MousePos;
            
            // Subtle bloom following cursor
            for (int i = 1; i <= 8; i++)
            {
                float alphaDecay = (float)Math.Pow(1.1f - (i / 8.0f), 3);
                uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.10f * alphaDecay));
                float radius = i * 4.0f;
                drawList.AddCircleFilled(mousePos, radius, gCol, 32);
            }
        }

        private void UpdateAndDrawRipples()
        {
            var drawList = ImGui.GetForegroundDrawList();
            float deltaTime = ImGui.GetIO().DeltaTime;
            
            for (int i = clickRipples.Count - 1; i >= 0; i--)
            {
                var ripple = clickRipples[i];
                ripple.Time += deltaTime * 2.0f; // Speed of expansion
                
                if (ripple.Time >= 1.0f)
                {
                    clickRipples.RemoveAt(i);
                    continue;
                }
                
                float t = ripple.Time;
                float radius = ripple.MaxRadius * t;
                float alpha = (1.0f - t) * 0.4f;
                uint col = ImGui.ColorConvertFloat4ToU32(new Vector4(ripple.Color.X, ripple.Color.Y, ripple.Color.Z, alpha));
                
                drawList.AddCircle(ripple.Position, radius, col, 32, 2.0f);
                
                // Add a second, thinner ring
                if (t < 0.8f)
                    drawList.AddCircle(ripple.Position, radius * 0.7f, ImGui.ColorConvertFloat4ToU32(new Vector4(ripple.Color.X, ripple.Color.Y, ripple.Color.Z, alpha * 0.5f)), 32, 1.0f);
            }
        }

        private bool AnimatedCheckbox(string label, ref bool v)
        {
            bool modified = false;
            if (!toggleAnimations.ContainsKey(label))
                toggleAnimations[label] = v ? 1.0f : 0.0f;

            var p = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            
            float width = 45.0f;
            float height = 24.0f;
            float radius = height * 0.5f;

            ImGui.InvisibleButton(label, new Vector2(width, height));
            if (ImGui.IsItemClicked())
            {
                v = !v;
                modified = true;
            }

            float target = v ? 1.0f : 0.0f;
            toggleAnimations[label] = Lerp(toggleAnimations[label], target, ImGui.GetIO().DeltaTime * 15.0f);
            float t = toggleAnimations[label];

            Vector4 bgOff = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 bgOn = accentColor;
            Vector4 bgColor = Vector4.Lerp(bgOff, bgOn, t);
            
            // Draw glow if toggled on
            if (t > 0.05f)
            {
                DrawGlowRect(p, p + new Vector2(width, height), accentColor, t * 1.0f, radius, 10);
            }
            
            drawList.AddRectFilled(p, new Vector2(p.X + width, p.Y + height), ImGui.ColorConvertFloat4ToU32(bgColor), radius);

            float circleRadius = radius - 3.0f;
            float circleX = p.X + radius + (width - radius * 2.0f) * t;
            drawList.AddCircleFilled(new Vector2(circleX, p.Y + radius), circleRadius, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)));

            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (height - ImGui.GetTextLineHeight()) * 0.5f);
            
            // Fix: Strip ## from the display label for a clean UI
            string displayLabel = label.Contains("##") ? label.Substring(0, label.IndexOf("##")) : label;
            ImGui.Text(displayLabel);

            return modified;
        }

        private void DrawSidebar()
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
            ImGui.BeginChild("Sidebar", new Vector2(160, 0), ImGuiChildFlags.None);
            ImGui.PopStyleColor();
            
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 15);
            ImGui.SetWindowFontScale(1.4f);
            ImGui.TextColored(accentColor, "Snow");
            ImGui.SetWindowFontScale(0.7f);
            ImGui.SameLine();
            ImGui.TextDisabled("BETA");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Dummy(new Vector2(0, 15));

            for (int i = 0; i < tabs.Length; i++)
            {
                if (!tabAnimations.ContainsKey(i))
                    tabAnimations[i] = (currentTab == i) ? 1.0f : 0.0f;

                float target = (currentTab == i) ? 1.0f : 0.0f;
                tabAnimations[i] = Lerp(tabAnimations[i], target, ImGui.GetIO().DeltaTime * 6.0f);
                float t = tabAnimations[i];

                var p = ImGui.GetCursorScreenPos();
                
                if (t > 0.01f)
                {
                    var drawList = ImGui.GetWindowDrawList();
                    Vector4 highlight = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, t * 0.15f);
                    
                    // Draw glowing border for the selected tab
                    if (currentTab == i)
                    {
                        DrawGlowRect(p, p + new Vector2(140, 35), accentColor, t * 0.5f, 17.5f, 8);
                    }
                    
                    drawList.AddRectFilled(p, new Vector2(p.X + 140, p.Y + 35), ImGui.ColorConvertFloat4ToU32(highlight), 17.5f);
                    
                    Vector4 barColor = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, t);
                    drawList.AddRectFilled(p, new Vector2(p.X + 4, p.Y + 35), ImGui.ColorConvertFloat4ToU32(barColor), 2.0f);
                    
                    // Add a tiny spark/glow at the top of the indicator bar
                    drawList.AddCircleFilled(new Vector2(p.X + 4, p.Y), 2.0f, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, t)));
                }

                Vector4 textColor = Vector4.Lerp(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), new Vector4(1f, 1f, 1f, 1f), t);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 0)); // Hide original text to draw glow manually
                ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
                
                if (ImGui.Selectable($"##{tabs[i]}", currentTab == i, ImGuiSelectableFlags.None, new Vector2(140, 35)))
                {
                    currentTab = i;
                }
                ImGui.SameLine();
                Vector2 textPos = new Vector2(p.X + 15 + t * 5, p.Y + 8);
                
                if (currentTab == i)
                {
                    DrawGlowText(textPos, accentColor, $"{tabIcons[i]}  {tabs[i]}", t);
                }
                else
                {
                    ImGui.GetWindowDrawList().AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), $"{tabIcons[i]}  {tabs[i]}");
                }

                ImGui.PopStyleColor(4);
                ImGui.Dummy(new Vector2(0, 5));
            }
            ImGui.EndChild();
        }

        private void SaveSettings()
        {
            var config = new ConfigData
            {
                UserName = userName,
                UsageData = usageData,
                ConfigNames = configNames,
                LastUsageDate = lastUsageDate,
                TotalTimeMinutes = totalTimeMinutes,
                MenuKey = menuKey,
                HideVisualsKey = hideVisualsKey,
                UseSplitBox = useSplitBox,
                EnableOutlines = enableOutlines,
                UseBoxGradient = useBoxGradient,
                BoxColor2 = V4ToArr(boxColor2),
                UseHeadDotGradient = useHeadDotGradient,
                HeadDotColor2 = V4ToArr(headDotColor2),
                UseHealthGradient = useHealthGradient,
                HealthBarColor2 = V4ToArr(healthBarColor2),
                UseFovGradient = useFovGradient,
                FovColor2 = V4ToArr(fovColor2),
                UseAccentGradient = useAccentGradient,
                AccentColor2 = V4ToArr(accentColor2),
                ShowBox = showBox,
                ShowSkeleton = showSkeleton,
                SkeletonColor = V4ToArr(skeletonColor),
                UseSkeletonGradient = useSkeletonGradient,
                SkeletonColor2 = V4ToArr(skeletonColor2),
                ShowBoxOutline = showBoxOutline,
                ShowHealthOutline = showHealthOutline,
                ShowSkeletonOutline = showSkeletonOutline,
                ShowFovOutline = showFovOutline,
                BoxOpacity = boxOpacity,
                HeadDotOpacity = headDotOpacity,
                HealthBarOpacity = healthBarOpacity,
                FovOpacity = fovOpacity,
                SoftAimKey = softAimKey,
                ShowWatermark = showWatermark,
                EnableGlow = enableGlow,
                GlowIntensity = glowIntensity,
                ShowBoxGlow = showBoxGlow,
                ShowSkeletonGlow = showSkeletonGlow,
                ShowHealthGlow = showHealthGlow,
                HealthBarPos = (int)healthBarPos,
                DistancePos = (int)distancePos,
                HealthTextPos = (int)healthTextPos
            };
            string json = JsonSerializer.Serialize(config);
            File.WriteAllText(settingsPath, json);
        }

        private void LoadSettings()
        {
            if (File.Exists(settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(settingsPath);
                    var config = JsonSerializer.Deserialize<ConfigData>(json);
                    if (config != null)
                    {
                        if (config.UserName != null) userName = config.UserName;
                        if (config.UsageData != null && config.UsageData.Length == 30) usageData = config.UsageData;
                        if (config.ConfigNames != null && config.ConfigNames.Length == configPaths.Length) configNames = config.ConfigNames;
                        totalTimeMinutes = config.TotalTimeMinutes;
                        lastUsageDate = config.LastUsageDate;
                        menuKey = config.MenuKey;
                        if (menuKey == 0x01 || menuKey == 0x02) menuKey = 0x72; // Reset if accidentally bound to mouse
                        hideVisualsKey = config.HideVisualsKey;
                        softAimKey = config.SoftAimKey;
                        if (softAimKey == 0) softAimKey = 0x43; // Default to C if 0
                        useSplitBox = config.UseSplitBox;
                        enableOutlines = config.EnableOutlines;
                        useBoxGradient = config.UseBoxGradient;
                        boxColor2 = ArrToV4(config.BoxColor2);
                        useHeadDotGradient = config.UseHeadDotGradient;
                        headDotColor2 = ArrToV4(config.HeadDotColor2);
                        useHealthGradient = config.UseHealthGradient;
                        healthBarColor2 = ArrToV4(config.HealthBarColor2);
                        useFovGradient = config.UseFovGradient;
                        fovColor2 = ArrToV4(config.FovColor2);
                        useAccentGradient = config.UseAccentGradient;
                        accentColor2 = ArrToV4(config.AccentColor2);
                        showBox = config.ShowBox;
                        showSkeleton = config.ShowSkeleton;
                        skeletonColor = ArrToV4(config.SkeletonColor);
                        useSkeletonGradient = config.UseSkeletonGradient;
                        skeletonColor2 = ArrToV4(config.SkeletonColor2);
                        showBoxOutline = config.ShowBoxOutline;
                        showHealthOutline = config.ShowHealthOutline;
                        showSkeletonOutline = config.ShowSkeletonOutline;
                        showFovOutline = config.ShowFovOutline;
                        boxOpacity = config.BoxOpacity;
                        headDotOpacity = config.HeadDotOpacity;
                        healthBarOpacity = config.HealthBarOpacity;
                        fovOpacity = config.FovOpacity;
                        showWatermark = config.ShowWatermark;
                        enableGlow = config.EnableGlow;
                        glowIntensity = config.GlowIntensity;
                        showBoxGlow = config.ShowBoxGlow;
                        showSkeletonGlow = config.ShowSkeletonGlow;
                        showHealthGlow = config.ShowHealthGlow;
                        healthBarPos = (ESPPosition)config.HealthBarPos;
                        distancePos = (ESPPosition)config.DistancePos;
                        healthTextPos = (ESPPosition)config.HealthTextPos;
                        
                        var today = DateTime.Now.Date;
                        var lastSaved = lastUsageDate.Date;
                        if (today > lastSaved)
                        {
                            var dayDiff = (int)(today - lastSaved).TotalDays;
                            for (int d = 0; d < dayDiff; d++)
                            {
                                for (int i = 0; i < 29; i++)
                                    usageData[i] = usageData[i + 1];
                                usageData[29] = 0;
                            }
                            lastUsageDate = today;
                            SaveSettings();
                        }
                    }
                }
                catch (JsonException) { InitDefaultSettings(); }
            }
            else { InitDefaultSettings(); }
        }

        private void InitDefaultSettings()
        {
            Random r = new Random();
            usageData = new float[30];
            for (int i = 0; i < 30; i++)
                usageData[i] = (float)r.NextDouble() * 5.0f + 2.0f;
            lastUsageDate = DateTime.Now.Date;
            totalTimeMinutes = 120.5f;
            SaveSettings();
        }

        private float[] V4ToArr(Vector4 v) => new float[] { v.X, v.Y, v.Z, v.W };
        private Vector4 ArrToV4(float[] a) => (a == null || a.Length < 4) ? new Vector4(1, 1, 1, 1) : new Vector4(a[0], a[1], a[2], a[3]);

        private void SaveConfig(int slot)
        {
            var config = new ConfigData
            {
                EnableESP = enableESP,
                ShowBox = showBox,
                ShowSkeleton = showSkeleton,
                SkeletonColor = V4ToArr(skeletonColor),
                UseSkeletonGradient = useSkeletonGradient,
                SkeletonColor2 = V4ToArr(skeletonColor2),
                ShowBoxOutline = showBoxOutline,
                ShowHealthOutline = showHealthOutline,
                ShowSkeletonOutline = showSkeletonOutline,
                ShowFovOutline = showFovOutline,
                UseSplitBox = useSplitBox,
                EnableOutlines = enableOutlines,
                ShowLines = showLines,
                ShowHeadDot = showHeadDot,
                ShowHealthBar = showHealthBar,
                BoxColor = V4ToArr(boxColor),
                UseBoxGradient = useBoxGradient,
                BoxColor2 = V4ToArr(boxColor2),
                BoxOpacity = boxOpacity,
                HeadDotColor = V4ToArr(headDotColor),
                UseHeadDotGradient = useHeadDotGradient,
                HeadDotColor2 = V4ToArr(headDotColor2),
                HeadDotOpacity = headDotOpacity,
                HealthBarColor = V4ToArr(healthBarColor),
                UseHealthGradient = useHealthGradient,
                HealthBarColor2 = V4ToArr(healthBarColor2),
                HealthBarOpacity = healthBarOpacity,
                FovColor = V4ToArr(fovColor),
                UseFovGradient = useFovGradient,
                FovColor2 = V4ToArr(fovColor2),
                FovOpacity = fovOpacity,
                AccentColor = V4ToArr(accentColor),
                UseAccentGradient = useAccentGradient,
                AccentColor2 = V4ToArr(accentColor2),
                MenuSnowflakes = menuSnowflakes,
                EspSnowflakes = espSnowflakes,
                EspSnowflakeDensity = espSnowflakeDensity,
                EspSnowflakeOpacity = espSnowflakeOpacity,
                EnableGlow = enableGlow,
                GlowIntensity = glowIntensity,
                ShowBoxGlow = showBoxGlow,
                ShowSkeletonGlow = showSkeletonGlow,
                ShowHealthGlow = showHealthGlow,
                EnableSoftAim = enableSoftAim,
                AimSmoothness = aimSmoothness,
                AimFov = aimFov,
                SoftAimKey = softAimKey,
                ShowFovCircle = showFovCircle,
                ShowWatermark = showWatermark,
                ShowDistance = showDistance,
                ShowHealthText = showHealthText,
                ShowBombTimer = showBombTimer,
                ShowTeam = showTeam,
                TeamBoxColor = V4ToArr(teamBoxColor),
                TeamBoxColor2 = V4ToArr(teamBoxColor2),
                UseTeamBoxGradient = useTeamBoxGradient,
                TeamSkeletonColor = V4ToArr(teamSkeletonColor),
                TeamSkeletonColor2 = V4ToArr(teamSkeletonColor2),
                UseTeamSkeletonGradient = useTeamSkeletonGradient,
                HealthBarPos = (int)healthBarPos,
                DistancePos = (int)distancePos,
                HealthTextPos = (int)healthTextPos
            };
            string json = JsonSerializer.Serialize(config);
            File.WriteAllText(configPaths[slot], json);
        }

        private void LoadConfig(int slot)
        {
            if (File.Exists(configPaths[slot]))
            {
                try
                {
                    string json = File.ReadAllText(configPaths[slot]);
                    var config = JsonSerializer.Deserialize<ConfigData>(json);
                    if (config != null)
                    {
                        enableESP = config.EnableESP;
                        showBox = config.ShowBox;
                        showSkeleton = config.ShowSkeleton;
                        skeletonColor = ArrToV4(config.SkeletonColor);
                        useSkeletonGradient = config.UseSkeletonGradient;
                        skeletonColor2 = ArrToV4(config.SkeletonColor2);
                        showBoxOutline = config.ShowBoxOutline;
                        showHealthOutline = config.ShowHealthOutline;
                        showSkeletonOutline = config.ShowSkeletonOutline;
                        showFovOutline = config.ShowFovOutline;
                        useSplitBox = config.UseSplitBox;
                        enableOutlines = config.EnableOutlines;
                        showLines = config.ShowLines;
                        showHeadDot = config.ShowHeadDot;
                        showHealthBar = config.ShowHealthBar;
                        boxColor = ArrToV4(config.BoxColor);
                        useBoxGradient = config.UseBoxGradient;
                        boxColor2 = ArrToV4(config.BoxColor2);
                        boxOpacity = config.BoxOpacity;
                        headDotColor = ArrToV4(config.HeadDotColor);
                        useHeadDotGradient = config.UseHeadDotGradient;
                        headDotColor2 = ArrToV4(config.HeadDotColor2);
                        headDotOpacity = config.HeadDotOpacity;
                        healthBarColor = ArrToV4(config.HealthBarColor);
                        useHealthGradient = config.UseHealthGradient;
                        healthBarColor2 = ArrToV4(config.HealthBarColor2);
                        healthBarOpacity = config.HealthBarOpacity;
                        fovColor = ArrToV4(config.FovColor);
                        useFovGradient = config.UseFovGradient;
                        fovColor2 = ArrToV4(config.FovColor2);
                        fovOpacity = config.FovOpacity;
                        accentColor = ArrToV4(config.AccentColor);
                        useAccentGradient = config.UseAccentGradient;
                        accentColor2 = ArrToV4(config.AccentColor2);
                        menuSnowflakes = config.MenuSnowflakes;
                        espSnowflakes = config.EspSnowflakes;
                        espSnowflakeDensity = config.EspSnowflakeDensity;
                        espSnowflakeOpacity = config.EspSnowflakeOpacity;
                        enableGlow = config.EnableGlow;
                        glowIntensity = config.GlowIntensity;
                        showBoxGlow = config.ShowBoxGlow;
                        showSkeletonGlow = config.ShowSkeletonGlow;
                        showHealthGlow = config.ShowHealthGlow;
                        enableSoftAim = config.EnableSoftAim;
                        aimSmoothness = config.AimSmoothness;
                        aimFov = config.AimFov;
                        softAimKey = config.SoftAimKey;
                        if (softAimKey == 0) softAimKey = 0x43;
                        showFovCircle = config.ShowFovCircle;
                        showWatermark = config.ShowWatermark;
                        showDistance = config.ShowDistance;
                        showHealthText = config.ShowHealthText;
                        showBombTimer = config.ShowBombTimer;
                        showTeam = config.ShowTeam;
                        teamBoxColor = ArrToV4(config.TeamBoxColor);
                        teamBoxColor2 = ArrToV4(config.TeamBoxColor2);
                        useTeamBoxGradient = config.UseTeamBoxGradient;
                        teamSkeletonColor = ArrToV4(config.TeamSkeletonColor);
                        teamSkeletonColor2 = ArrToV4(config.TeamSkeletonColor2);
                        useTeamSkeletonGradient = config.UseTeamSkeletonGradient;
                        healthBarPos = (ESPPosition)config.HealthBarPos;
                        distancePos = (ESPPosition)config.DistancePos;
                        healthTextPos = (ESPPosition)config.HealthTextPos;
                        ApplyStyle();
                    }
                }
                catch (JsonException) { }
            }
        }

        private void DrawWaveGraph(Vector2 size)
        {
            var p = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            float time = (float)ImGui.GetTime();

            drawList.AddRectFilled(p, p + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 0.5f)), 8.0f);
            
            float barWidth = size.X / 30.0f;
            for (int i = 0; i < 30; i++)
            {
                float barDelay = i * 0.02f;
                float barGrow = Math.Max(0, (homeFadeAlpha - barDelay) * 1.5f);
                if (barGrow > 1.0f) barGrow = 1.0f;

                float val = (usageData != null && i < usageData.Length) ? usageData[i] : 0.5f;
                if (val < 0.5f) val = 0.5f;

                float animatedVal = val + (float)Math.Sin(time * 2.0f + i * 0.5f) * 0.3f;
                float barHeight = (animatedVal / 10.0f) * size.Y * barGrow;
                
                Vector2 barMin = new Vector2(p.X + i * barWidth + 2, p.Y + size.Y - barHeight);
                Vector2 barMax = new Vector2(p.X + (i + 1) * barWidth - 2, p.Y + size.Y);
                
                Vector4 col = Vector4.Lerp(accentColor, new Vector4(1, 1, 1, 1), (float)i / 30.0f * 0.3f);
                drawList.AddRectFilled(barMin, barMax, ImGui.ColorConvertFloat4ToU32(col), 2.0f);
            }

            if (usageData != null && usageData.Length >= 30)
            {
                for (int i = 0; i < 29; i++)
                {
                    float barDelay1 = i * 0.02f;
                    float barGrow1 = Math.Max(0, (homeFadeAlpha - barDelay1) * 1.5f);
                    if (barGrow1 > 1.0f) barGrow1 = 1.0f;

                    float barDelay2 = (i+1) * 0.02f;
                    float barGrow2 = Math.Max(0, (homeFadeAlpha - barDelay2) * 1.5f);
                    if (barGrow2 > 1.0f) barGrow2 = 1.0f;

                    float val1 = (usageData[i] < 0.5f ? 0.5f : usageData[i]) + (float)Math.Sin(time * 2.0f + i * 0.5f) * 0.3f;
                    float val2 = (usageData[i+1] < 0.5f ? 0.5f : usageData[i+1]) + (float)Math.Sin(time * 2.0f + (i+1) * 0.5f) * 0.3f;
                    
                    Vector2 p1 = new Vector2(p.X + i * barWidth + barWidth/2, p.Y + size.Y - (val1 / 10.0f) * size.Y * barGrow1);
                    Vector2 p2 = new Vector2(p.X + (i+1) * barWidth + barWidth/2, p.Y + size.Y - (val2 / 10.0f) * size.Y * barGrow2);
                    
                    drawList.AddLine(p1, p2, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.4f * barGrow1)), 2.0f);
                }
            }
            ImGui.Dummy(size);
        }

        private void DrawHome()
        {
            homeFadeAlpha = Lerp(homeFadeAlpha, 1.0f, ImGui.GetIO().DeltaTime * 2.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, homeFadeAlpha);
            
            if (!offsetsUpdated)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 0.9f));
                ImGui.TextWrapped("You must update offsets every time you launch the game for the cheat to work. (Takes only 1 second)");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            var availableSpace = ImGui.GetContentRegionAvail();
            
            ImGui.BeginGroup();
            ImGui.SetWindowFontScale(1.5f);
            ImGui.TextColored(accentColor, "Welcome back,");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 1, 1, 1), userName);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.TextDisabled($"Total Usage Tracker: {((int)totalTimeMinutes / 60)}h {((int)totalTimeMinutes % 60)}m");
            ImGui.EndGroup();

            ImGui.SameLine(availableSpace.X - 150); 
            ImGui.BeginGroup();
            ImGui.TextDisabled("Name");
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputText("##username", ref userName, 32)) SaveSettings();
            ImGui.EndGroup();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            ImGui.BeginGroup();
            ImGui.Text("USAGE INTENSITY (LAST 30 DAYS)");
            DrawWaveGraph(new Vector2(availableSpace.X - 230, 180));
            ImGui.EndGroup();

            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text("PRESETS");
            ImGui.BeginChild("PresetList", new Vector2(210, 180), ImGuiChildFlags.None);
            ImGui.Dummy(new Vector2(0, 5)); 
            for (int i = 0; i < 3; i++)
            {
                ImGui.PushID(i);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10); 
                ImGui.SetNextItemWidth(110);
                ImGui.InputText("##confname", ref configNames[i], 16);
                ImGui.SameLine();
                if (ImGui.Button("S")) SaveConfig(i);
                ImGui.SameLine();
                if (ImGui.Button("L")) LoadConfig(i);
                ImGui.PopID();
                ImGui.Spacing();
            }
            ImGui.EndChild();
            ImGui.EndGroup();
            ImGui.PopStyleVar();
        }

        private void ApplyStyle()
        {
            ImGuiStylePtr style = ImGui.GetStyle();
            style.WindowRounding = 12.0f;
            style.ChildRounding = 8.0f;
            style.FrameRounding = 6.0f;
            style.PopupRounding = 8.0f;
            style.ScrollbarRounding = 8.0f;
            style.GrabRounding = 12.0f;
            style.GrabMinSize = 10.0f;
            style.TabRounding = 6.0f;
            style.WindowTitleAlign = new Vector2(0.5f, 0.5f);

            var colors = style.Colors;
            colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.90f, 0.93f, 1.00f);
            Vector4 brightAccent = useAccentGradient ? accentColor2 : accentColor;
            colors[(int)ImGuiCol.Header] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.3f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.5f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.8f);
            colors[(int)ImGuiCol.CheckMark] = brightAccent;
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.4f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.7f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.15f, 0.15f, 0.18f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.14f, 0.14f, 0.17f, 1.00f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.4f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.6f);
            
            style.WindowPadding = new Vector2(16.0f, 16.0f);
            style.FramePadding = new Vector2(10.0f, 6.0f);
            style.ItemSpacing = new Vector2(10.0f, 10.0f);
        }

        protected override void Render()
        {
            EnsureFonts();
            
            if (!styleApplied)
            {
                LoadSettings();
                ApplyStyle();
                
                ImGui.GetIO().FontGlobalScale = 1.0f;
                
                styleApplied = true;
            }


            short hideKeyState = GetAsyncKeyState(hideVisualsKey);
            bool isHidePressed = (hideKeyState & 0x8000) != 0;
            if (isHidePressed && wasHideKeyReleased)
            {
                isVisualsHidden = !isVisualsHidden;
                wasHideKeyReleased = false;
            }
            else if (!isHidePressed) wasHideKeyReleased = true;

            if (remappingKeyIndex != -1)
            {
                for (int i = 0x03; i <= 0xFE; i++)
                {
                    if ((GetAsyncKeyState(i) & 0x8000) != 0)
                    {
                        if (remappingKeyIndex == 0) menuKey = i;
                        else if (remappingKeyIndex == 1) hideVisualsKey = i;
                        else if (remappingKeyIndex == 2) softAimKey = i;
                        remappingKeyIndex = -1;
                        SaveSettings();
                        break;
                    }
                }
            }

            short menuKeyState = GetAsyncKeyState(menuKey);
            bool isMenuPressed = (menuKeyState & 0x8000) != 0;
            if (isMenuPressed && !wasInsertPressed && remappingKeyIndex == -1 && menuKey > 0x02) showMenu = !showMenu;
            wasInsertPressed = isMenuPressed;

            if (showMenu) 
            {
                // Detect click for ripple effect
                if (ImGui.IsMouseClicked(0))
                {
                    clickRipples.Add(new ClickRipple { 
                        Position = ImGui.GetIO().MousePos, 
                        Time = 0, 
                        MaxRadius = 60.0f, 
                        Color = accentColor 
                    });
                }
                DrawMenu();
                DrawCursorGlow();
                UpdateAndDrawRipples();
            }

            if (!isVisualsHidden) DrawOverlay();
        }




        private void DrawMenu()
        {
            ImGui.SetNextWindowSize(new Vector2(850, 550), ImGuiCond.FirstUseEver);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0)); 
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, new Vector4(0, 0, 0, 0));
            // Interaction Lock Logic - Calculated BEFORE ImGui.Begin to ensure frame-perfect NoMove flag
            Vector2 mPos = ImGui.GetIO().MousePos;
            bool mouseInPreview = mPos.X > lastMenuPos.X + 580 && mPos.Y > lastMenuPos.Y + 42;
            
            ImGuiWindowFlags menuFlags = ImGuiWindowFlags.NoTitleBar;
            if ((isInteractingWithPreview || draggingHealthBar || draggingDistance || draggingHPText) || (ImGui.IsMouseDown(0) && mouseInPreview)) 
            {
                menuFlags |= ImGuiWindowFlags.NoMove;
            }
            
            ImGui.Begin("Snow Window", menuFlags);
            lastMenuPos = ImGui.GetWindowPos(); // Ensure we have current pos for logic and next frame

            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();

            float headerHeight = 42.0f;
            float rounding = 12.0f;
            uint mainBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.07f, 0.98f));
            uint headColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.14f, 1.0f));
            uint accentBorderCol = ImGui.ColorConvertFloat4ToU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.25f));
            uint shadowCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.4f));

            drawList.AddRectFilled(windowPos + new Vector2(3, 3), windowPos + windowSize + new Vector2(3, 3), shadowCol, rounding);
            drawList.AddRectFilled(windowPos, windowPos + windowSize, mainBg, rounding);
            drawList.AddRect(windowPos, windowPos + windowSize, accentBorderCol, rounding, ImDrawFlags.None, 1.5f);
            drawList.AddRectFilled(windowPos, windowPos + new Vector2(windowSize.X, headerHeight), headColor, rounding, ImDrawFlags.RoundCornersTop);
            
            // Bright glowing line at the top separator
            DrawGlowLine(windowPos + new Vector2(0, headerHeight), windowPos + new Vector2(windowSize.X, headerHeight), accentColor, 1.2f, 12);
            drawList.AddLine(windowPos + new Vector2(0, headerHeight), windowPos + new Vector2(windowSize.X, headerHeight), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.4f)), 1.5f);

            ImGui.SetCursorPos(new Vector2(16, 12));
            ImGui.SetWindowFontScale(1.1f);
            DrawGlowText(windowPos + new Vector2(16, 12), accentColor, "Snow", 1.5f);
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.CalcTextSize("Snow").X + 5);
            ImGui.SameLine(); 
            ImGui.SetWindowFontScale(0.8f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.6f, 1.0f), "BETA");
            ImGui.SetWindowFontScale(1.0f);

            ImGui.SetCursorPos(new Vector2(windowSize.X - 35, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12.0f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 0.4f));
            if (ImGui.Button("X", new Vector2(24, 24))) { SaveSettings(); Environment.Exit(0); }
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);

            InitSnowflakes();
            if (menuSnowflakes) DrawSnowflakes(windowPos, windowSize);

            ImGui.SetCursorPos(new Vector2(10, 50));
            DrawSidebar(); ImGui.SameLine();
            ImGui.BeginChild("HomeContent", new Vector2(0, 0), ImGuiChildFlags.None);
            
            if (currentTab == 0) DrawHome();
            else if (currentTab == 1) DrawVisuals();
            else if (currentTab == 2) DrawColors();
            else if (currentTab == 3) DrawAimbot();
            else if (currentTab == 4) DrawMisc();
            else if (currentTab == 5) DrawKeybinds();
            else if (currentTab == 6) DrawUpdate();


            ImGui.EndChild(); ImGui.End();
            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar(3); 
        }

        private void DrawUpdate()
        {
            ImGui.SetWindowFontScale(1.3f);
            ImGui.TextColored(accentColor, "Offset Updater");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.TextDisabled("Fetch latest offsets from a2x/cs2-dumper GitHub repository.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var availableSpace = ImGui.GetContentRegionAvail();
            
            ImGui.BeginChild("UpdateStatus", new Vector2(availableSpace.X, 120), ImGuiChildFlags.None);
            ImGui.SetCursorPos(new Vector2(20, 20));
            ImGui.Text("Current Status:");
            ImGui.SetCursorPos(new Vector2(20, 45));
            ImGui.SetWindowFontScale(1.2f);
            
            Vector4 statusCol = updateStatus.Contains("Error") || updateStatus.Contains("failed") ? new Vector4(1, 0, 0, 1) : 
                               updateStatus.Contains("successfully") ? new Vector4(0, 1, 0, 1) : 
                               new Vector4(1, 1, 1, 1);
            
            if (isUpdating) statusCol = accentColor;
            
            ImGui.TextColored(statusCol, updateStatus);
            ImGui.SetWindowFontScale(1.0f);
            
            if (isUpdating)
            {
                float time = (float)ImGui.GetTime();
                string loading = new string('.', (int)(time * 2) % 4);
                ImGui.SameLine();
                ImGui.Text(loading);
            }
            else if (updateDetail != "")
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Update Results:");
                ImGui.BeginChild("UpdateDetailsChild", new Vector2(0, 80), ImGuiChildFlags.None);
                ImGui.TextWrapped(updateDetail);
                ImGui.EndChild();
            }
            ImGui.EndChild();

            ImGui.Spacing();
            
            float buttonWidth = availableSpace.X - 40;
            ImGui.SetCursorPosX(20);
            if (ImGui.Button("Update Offsets from GitHub", new Vector2(buttonWidth, 45)) && !isUpdating)
            {
                isUpdating = true;
                updateStatus = "Downloading latest offsets...";
                Task.Run(async () => {
                    (bool success, string message, string detail) = await OffsetManager.UpdateFromGitHub();
                    updateStatus = message;
                    updateDetail = detail;
                    if (success) offsetsUpdated = true;
                    isUpdating = false;
                });
            }


            ImGui.TextWrapped("Note: Updating offsets will overwrite your local offsets_config.json. The changes take effect immediately, but a restart is recommended if pointers seem broken.");
        }

        private void DrawVisuals()
        {
            ImGui.Columns(2, "visuals_cols", false); ImGui.SetColumnWidth(0, 580);
            ImGui.BeginChild("VisualsList", new Vector2(0, 0), ImGuiChildFlags.None);
            ImGui.TextColored(accentColor, "ESP VISUALS"); ImGui.Separator();
            
            // Start a table for aligned checkboxes
            if (ImGui.BeginTable("visuals_settings_table", 2, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("col1", ImGuiTableColumnFlags.WidthFixed, 280);
                ImGui.TableSetupColumn("col2", ImGuiTableColumnFlags.WidthFixed, 280);
                ImGui.TableNextColumn();
                AnimatedCheckbox("Enable ESP", ref enableESP);
                AnimatedCheckbox("Box ESP", ref showBox);
                AnimatedCheckbox("Head Dot", ref showHeadDot);
                AnimatedCheckbox("Skeleton ESP", ref showSkeleton);
                AnimatedCheckbox("Health Bar", ref showHealthBar);
                AnimatedCheckbox("Team ESP", ref showTeam);

                ImGui.TableNextColumn();
                AnimatedCheckbox("Health Text", ref showHealthText);
                AnimatedCheckbox("Snap Lines", ref showLines);
                AnimatedCheckbox("Distance ESP", ref showDistance);
                AnimatedCheckbox("Bomb Timer", ref showBombTimer);
                AnimatedCheckbox("ESP Snowflakes", ref espSnowflakes);
                AnimatedCheckbox("Split Box ESP Style", ref useSplitBox);

                ImGui.EndTable();
            }

            ImGui.Dummy(new Vector2(0, 5)); ImGui.Separator();
            ImGui.Text("Match Atmosphere");
            AnimatedCheckbox("ESP Snowflakes", ref espSnowflakes);
            if (espSnowflakes) {
                ImGui.SliderInt("Density", ref espSnowflakeDensity, 10, 500);
                ImGui.SliderFloat("Opacity", ref espSnowflakeOpacity, 0.1f, 1.0f);
            }
            
            ImGui.EndChild();
            ImGui.NextColumn(); DrawESPPreview(); ImGui.Columns(1);
        }

        private void DrawColors()
        {
            ImGui.Columns(2, "colors_cols", false); ImGui.SetColumnWidth(0, 580);
            ImGui.BeginChild("ColorsList", new Vector2(0, 0), ImGuiChildFlags.None);
            ImGui.TextColored(accentColor, "COLORS & THEME"); ImGui.Separator();
            AnimatedCheckbox("Global Outlines", ref enableOutlines);
            
            ImGui.Text("Enemy Colors");
            if (ImGui.BeginTable("enemy_colors_table", 3, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Attr", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableSetupColumn("GradTog", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("GradCol", ImGuiTableColumnFlags.WidthFixed, 60);

                ImGui.TableNextColumn(); ImGui.ColorEdit4("Box Enemy", ref boxColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("Box Grad", ref useBoxGradient);
                ImGui.TableNextColumn(); if (useBoxGradient) ImGui.ColorEdit4("##box2", ref boxColor2, ImGuiColorEditFlags.NoInputs);

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.ColorEdit4("Skeleton Enemy", ref skeletonColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("Skel Grad", ref useSkeletonGradient);
                ImGui.TableNextColumn(); if (useSkeletonGradient) ImGui.ColorEdit4("##skel2", ref skeletonColor2, ImGuiColorEditFlags.NoInputs);
                ImGui.EndTable();
            }

            ImGui.Separator(); ImGui.Text("Team Colors");
            if (ImGui.BeginTable("team_colors_table", 3, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Attr", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableSetupColumn("GradTog", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("GradCol", ImGuiTableColumnFlags.WidthFixed, 60);

                ImGui.TableNextColumn(); ImGui.ColorEdit4("Box Team", ref teamBoxColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("T-Box Grad", ref useTeamBoxGradient);
                ImGui.TableNextColumn(); if (useTeamBoxGradient) ImGui.ColorEdit4("##tbox2", ref teamBoxColor2, ImGuiColorEditFlags.NoInputs);

                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.ColorEdit4("Skeleton Team", ref teamSkeletonColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("T-Skel Grad", ref useTeamSkeletonGradient);
                ImGui.TableNextColumn(); if (useTeamSkeletonGradient) ImGui.ColorEdit4("##tskel2", ref teamSkeletonColor2, ImGuiColorEditFlags.NoInputs);
                ImGui.EndTable();
            }

            ImGui.Separator();
            ImGui.Text("Health Bar");
            if (ImGui.BeginTable("health_bar_table", 4, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Attr", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableSetupColumn("GradTog", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("GradCol", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("OutTog", ImGuiTableColumnFlags.WidthFixed, 100);

                ImGui.TableNextColumn(); ImGui.ColorEdit4("Color##hcol", ref healthBarColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("Gradient##health", ref useHealthGradient);
                ImGui.TableNextColumn(); if (useHealthGradient) ImGui.ColorEdit4("##health2", ref healthBarColor2, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("Outline##healthout", ref showHealthOutline);
                ImGui.EndTable();
            }
            ImGui.SliderFloat("Health Opacity", ref healthBarOpacity, 0.0f, 1.0f);

            ImGui.Text("FOV Circle");
            if (ImGui.BeginTable("fov_circle_table", 4, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Attr", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableSetupColumn("GradTog", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("GradCol", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("OutTog", ImGuiTableColumnFlags.WidthFixed, 100);

                ImGui.TableNextColumn(); ImGui.ColorEdit4("Color##fovcol", ref fovColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("Gradient##fov", ref useFovGradient);
                ImGui.TableNextColumn(); if (useFovGradient) ImGui.ColorEdit4("##fov2", ref fovColor2, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn(); AnimatedCheckbox("Outline##fovout", ref showFovOutline);
                ImGui.EndTable();
            }
            ImGui.SliderFloat("FOV Opacity", ref fovOpacity, 0.0f, 1.0f);
            
            ImGui.Dummy(new Vector2(0, 10)); ImGui.Separator();

            ImGui.Text("Menu Theme");
            if (ImGui.BeginTable("menu_theme_table", 2, ImGuiTableFlags.None))
            {
                ImGui.TableNextColumn();
                ImGui.ColorEdit4("Accent Color", ref accentColor, ImGuiColorEditFlags.NoInputs);
                ImGui.TableNextColumn();
                AnimatedCheckbox("Menu Gradient", ref useAccentGradient);
                if (useAccentGradient) { ImGui.SameLine(); ImGui.ColorEdit4("##accent2", ref accentColor2, ImGuiColorEditFlags.NoInputs); }
                ImGui.EndTable();
            }

            ImGui.Separator(); ImGui.Text("Global Glow Settings");
            AnimatedCheckbox("Master Glow Enable", ref enableGlow);
            if (enableGlow) {
                ImGui.SliderFloat("Glow Intensity", ref glowIntensity, 1.0f, 10.0f);
                if (ImGui.BeginTable("glow_elements_table", 3, ImGuiTableFlags.None))
                {
                    ImGui.TableNextColumn(); AnimatedCheckbox("Box Glow", ref showBoxGlow);
                    ImGui.TableNextColumn(); AnimatedCheckbox("Skel Glow", ref showSkeletonGlow);
                    ImGui.TableNextColumn(); AnimatedCheckbox("HP Glow", ref showHealthGlow);
                    ImGui.EndTable();
                }
            }
            ImGui.EndChild();

            ImGui.NextColumn(); DrawESPPreview(); ImGui.Columns(1);
        }

        private void DrawAimbot()
        {
            ImGui.Columns(2, "aimbot_cols", false); ImGui.SetColumnWidth(0, 580);
            ImGui.BeginChild("AimbotList", new Vector2(0, 0), ImGuiChildFlags.None);
            ImGui.TextColored(accentColor, "SOFT AIMBOT"); ImGui.Separator();
            AnimatedCheckbox("Enable SoftAim", ref enableSoftAim);
            ImGui.SliderFloat("Smoothness", ref aimSmoothness, 1.0f, 20.0f);
            ImGui.SliderFloat("FOV Radius", ref aimFov, 10.0f, 500.0f);
            AnimatedCheckbox("Draw FOV Circle", ref showFovCircle);
            ImGui.EndChild();
            ImGui.NextColumn(); DrawFOVPreview(); ImGui.Columns(1);
        }

        private void DrawMisc()
        {
            ImGui.TextColored(accentColor, "MISCELLANEOUS"); ImGui.Separator();
            ImGui.Text("Atmosphere");
            AnimatedCheckbox("Menu Snowflakes", ref menuSnowflakes);
            AnimatedCheckbox("Show Watermark", ref showWatermark);
        }

        private Vector2 GetStackedPos(ESPPosition pos, Vector2 size, Vector2 rectTop, Vector2 rectBottom, float p_width, float p_height, ref float currentOffset)
        {
            float baseOffset = 6.0f; // Uniform base gap
            float effectiveOffset = baseOffset + currentOffset;
            Vector2 res = Vector2.Zero;
            switch (pos)
            {
                case ESPPosition.Left_Top: res = new Vector2(rectTop.X - size.X - effectiveOffset, rectTop.Y); break;
                case ESPPosition.Left_Middle: res = new Vector2(rectTop.X - size.X - effectiveOffset, rectTop.Y + p_height / 2 - size.Y / 2); break;
                case ESPPosition.Left_Bottom: res = new Vector2(rectTop.X - size.X - effectiveOffset, rectBottom.Y - size.Y); break;
                case ESPPosition.Right_Top: res = new Vector2(rectBottom.X + effectiveOffset, rectTop.Y); break;
                case ESPPosition.Right_Middle: res = new Vector2(rectBottom.X + effectiveOffset, rectTop.Y + p_height / 2 - size.Y / 2); break;
                case ESPPosition.Right_Bottom: res = new Vector2(rectBottom.X + effectiveOffset, rectBottom.Y - size.Y); break;
                case ESPPosition.Top_Left: res = new Vector2(rectTop.X, rectTop.Y - size.Y - effectiveOffset); break;
                case ESPPosition.Top_Middle: res = new Vector2(rectTop.X + p_width / 2 - size.X / 2, rectTop.Y - size.Y - effectiveOffset); break;
                case ESPPosition.Top_Right: res = new Vector2(rectBottom.X - size.X, rectTop.Y - size.Y - effectiveOffset); break;
                case ESPPosition.Bottom_Left: res = new Vector2(rectTop.X, rectBottom.Y + effectiveOffset); break;
                case ESPPosition.Bottom_Middle: res = new Vector2(rectTop.X + p_width / 2 - size.X / 2, rectBottom.Y + effectiveOffset); break;
                case ESPPosition.Bottom_Right: res = new Vector2(rectBottom.X - size.X, rectBottom.Y + effectiveOffset); break;
                default: res = rectTop; break;
            }
            if ((int)pos < 6) currentOffset += size.X + 4; else currentOffset += size.Y + 4;
            return res;
        }

        private void DrawKeybinds()
        {
            ImGui.TextColored(accentColor, "KEYBINDS"); ImGui.Separator();
            if (ImGui.Button ($"Menu: {GetKeyName(menuKey)}")) remappingKeyIndex = 0;
            if (ImGui.Button($"Hide Visuals: {GetKeyName(hideVisualsKey)}")) remappingKeyIndex = 1;
            if (ImGui.Button($"SoftAim Key: {GetKeyName (softAimKey)}")) remappingKeyIndex = 2;
        }


        private unsafe void DrawESPPreview()
        {
            ImGui.BeginChild("ESPPreview", new Vector2(0, 440), ImGuiChildFlags.None);
            var p_windowPos = ImGui.GetWindowPos(); var p_windowSize = ImGui.GetWindowSize(); var p_drawList = ImGui.GetWindowDrawList();
            // Removed grey background box
            Vector2 previewCenter = p_windowPos + (p_windowSize / 2);
            
            float p_height = p_windowSize.Y * 0.55f; float p_width = p_height / 2.0f;
            Vector2 headPos = new Vector2(previewCenter.X, previewCenter.Y - p_height / 1.8f);
            Vector2 footPos = new Vector2(previewCenter.X, previewCenter.Y + p_height / 2.3f);
            Vector2 rectTop = new Vector2(previewCenter.X - p_width / 2, headPos.Y);
            Vector2 rectBottom = new Vector2(previewCenter.X + p_width / 2, footPos.Y);
            uint uCol1 = ImGui.ColorConvertFloat4ToU32(new Vector4(boxColor.X, boxColor.Y, boxColor.Z, boxOpacity));
            uint uCol2 = ImGui.ColorConvertFloat4ToU32(new Vector4(boxColor2.X, boxColor2.Y, boxColor2.Z, boxOpacity));
            uint uOutline = GetOutlineColor();

            // Mouse interaction and Snapping Logic
            Vector2 mousePos = ImGui.GetMousePos();
            bool isMouseDown = ImGui.IsMouseDown(0);

            if (isMouseDown && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) isInteractingWithPreview = true;
            if (!isMouseDown) isInteractingWithPreview = false;

            float zS = 25.0f; // Zone size
            
            // Define 12 Snap Zones (3 per side)
            Rect[] snapZones = new Rect[12];
            // Left Side
            snapZones[(int)ESPPosition.Left_Top] = new Rect(rectTop.X - zS, rectTop.Y, zS, p_height / 3);
            snapZones[(int)ESPPosition.Left_Middle] = new Rect(rectTop.X - zS, rectTop.Y + p_height / 3, zS, p_height / 3);
            snapZones[(int)ESPPosition.Left_Bottom] = new Rect(rectTop.X - zS, rectTop.Y + 2 * p_height / 3, zS, p_height / 3);
            // Right Side
            snapZones[(int)ESPPosition.Right_Top] = new Rect(rectBottom.X, rectTop.Y, zS, p_height / 3);
            snapZones[(int)ESPPosition.Right_Middle] = new Rect(rectBottom.X, rectTop.Y + p_height / 3, zS, p_height / 3);
            snapZones[(int)ESPPosition.Right_Bottom] = new Rect(rectBottom.X, rectTop.Y + 2 * p_height / 3, zS, p_height / 3);
            // Top Side
            snapZones[(int)ESPPosition.Top_Left] = new Rect(rectTop.X, rectTop.Y - zS, p_width / 3, zS);
            snapZones[(int)ESPPosition.Top_Middle] = new Rect(rectTop.X + p_width / 3, rectTop.Y - zS, p_width / 3, zS);
            snapZones[(int)ESPPosition.Top_Right] = new Rect(rectTop.X + 2 * p_width / 3, rectTop.Y - zS, p_width / 3, zS);
            // Bottom Side
            snapZones[(int)ESPPosition.Bottom_Left] = new Rect(rectTop.X, rectBottom.Y, p_width / 3, zS);
            snapZones[(int)ESPPosition.Bottom_Middle] = new Rect(rectTop.X + p_width / 3, rectBottom.Y, p_width / 3, zS);
            snapZones[(int)ESPPosition.Bottom_Right] = new Rect(rectTop.X + 2 * p_width / 3, rectBottom.Y, p_width / 3, zS);

            void DrawZoneIndicator(Rect r, bool active) {
                p_drawList.AddRectFilled(r.Min, r.Max, active ? 0x88FFCC00u : 0x11FFFFFFu, 4.0f);
                if (active) p_drawList.AddRect(r.Min, r.Max, 0xFFFFFFFFu, 4.0f, ImDrawFlags.None, 1.5f);
            }

            if (draggingHealthBar || draggingDistance || draggingHPText) {
                for (int i = 0; i < 12; i++) DrawZoneIndicator(snapZones[i], snapZones[i].Contains(mousePos));
            }

            // Snap Zones and Stacking Logic for Preview
            float[] previewOffsets = new float[12];

            // Re-define GetMockPos to support stacking
            Vector2 GetMockStackedPos(ESPPosition pos, Vector2 size, out bool isH) {
                isH = (int)pos >= 6;
                return GetStackedPos(pos, size, rectTop, rectBottom, p_width, p_height, ref previewOffsets[(int)pos]);
            }

            if (showBoxGlow && enableGlow)
            {
                for (int i = 1; i <= 10; i++)
                {
                    float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                    uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(boxColor.X, boxColor.Y, boxColor.Z, 0.15f * alphaDecay));
                    float spread = (glowIntensity * i) * 0.4f;
                    p_drawList.AddRect(rectTop - new Vector2(spread, spread), rectBottom + new Vector2(spread, spread), gCol, 0, ImDrawFlags.None, 1.0f + spread);
                }
            }

            if (showBox) {
                if (useSplitBox) {
                    float lW = p_width / 4; float lH = p_height / 4;
                    void DB(Vector2 p1, Vector2 p2) { if (enableOutlines && showBoxOutline) p_drawList.AddLine(p1, p2, uOutline, 3.5f); p_drawList.AddLine(p1, p2, uCol1, 2.0f); }
                    DB(rectTop, new Vector2(rectTop.X + lW, rectTop.Y)); DB(rectTop, new Vector2(rectTop.X, rectTop.Y + lH));
                    DB(new Vector2(rectBottom.X, rectTop.Y), new Vector2(rectBottom.X - lW, rectTop.Y)); DB(new Vector2(rectBottom.X, rectTop.Y), new Vector2(rectBottom.X, rectTop.Y + lH));
                    DB(new Vector2(rectTop.X, rectBottom.Y), new Vector2(rectTop.X + lW, rectBottom.Y)); DB(new Vector2(rectTop.X, rectBottom.Y), new Vector2(rectTop.X, rectBottom.Y - lH));
                    DB(rectBottom, new Vector2(rectBottom.X - lW, rectBottom.Y)); DB(rectBottom, new Vector2(rectBottom.X, rectBottom.Y - lH));
                } else {
                    if (enableOutlines && showBoxOutline) p_drawList.AddRect(rectTop, rectBottom, uOutline, 0, ImDrawFlags.None, 4.0f);
                    if (useBoxGradient) p_drawList.AddRectFilledMultiColor(rectTop, rectBottom, uCol2, uCol2, uCol1, uCol1);
                    p_drawList.AddRect(rectTop, rectBottom, uCol1, 0, ImDrawFlags.None, 2.0f);
                }
            }

            if (showHealthBar) {
                float bT = 4.0f; Vector2 bTop = Vector2.Zero, bBottom = Vector2.Zero; bool horiz = false;
                
                // Restrict Health Bar to major sides in Preview dragging
                if (draggingHealthBar) {
                    for (int i = 0; i < 12; i++) {
                        bool isCenter = i == (int)ESPPosition.Left_Middle || i == (int)ESPPosition.Right_Middle || 
                                       i == (int)ESPPosition.Top_Middle || i == (int)ESPPosition.Bottom_Middle;
                        if (isCenter && snapZones[i].Contains(mousePos)) { /* valid snap */ }
                        else if (!isMouseDown && snapZones[i].Contains(mousePos)) { 
                           // Force centered positions for health bar
                           if (i < 3) healthBarPos = ESPPosition.Left_Middle;
                           else if (i < 6) healthBarPos = ESPPosition.Right_Middle;
                           else if (i < 9) healthBarPos = ESPPosition.Top_Middle;
                           else healthBarPos = ESPPosition.Bottom_Middle;
                        }
                    }
                }

                Vector2 hSize = ((int)healthBarPos >= 6) ? new Vector2(p_width, bT) : new Vector2(bT, p_height);
                Vector2 actualPos = GetMockStackedPos(healthBarPos, hSize, out horiz);
                bTop = actualPos; bBottom = actualPos + hSize;

                if (draggingHealthBar) {
                    Vector2 dragPos = mousePos - dragHBOffset;
                    bTop = dragPos; bBottom = dragPos + hSize;
                    if (!isMouseDown) { draggingHealthBar = false; SaveSettings(); }
                }

                Rect hHitbox = new Rect(bTop - new Vector2(15,15), bBottom + new Vector2(15,15));
                if (isMouseDown && hHitbox.Contains(mousePos) && !draggingDistance && !draggingHPText && !draggingHealthBar) {
                    draggingHealthBar = true; dragHBOffset = mousePos - bTop;
                }
                
                uint hCol1 = ImGui.ColorConvertFloat4ToU32(new Vector4(healthBarColor.X, healthBarColor.Y, healthBarColor.Z, healthBarOpacity));
                uint hCol2 = ImGui.ColorConvertFloat4ToU32(new Vector4(healthBarColor2.X, healthBarColor2.Y, healthBarColor2.Z, healthBarOpacity));
                
                if (showHealthGlow && enableGlow) {
                    for (int i = 1; i <= 10; i++) {
                        float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                        uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(healthBarColor.X, healthBarColor.Y, healthBarColor.Z, 0.15f * alphaDecay));
                        float spread = (glowIntensity * i) * 0.3f;
                        p_drawList.AddRectFilled(bTop - new Vector2(spread, spread), bBottom + new Vector2(spread, spread), gCol, 0.0f);
                    }
                }

                if (enableOutlines && showHealthOutline) p_drawList.AddRectFilled(bTop - new Vector2(1,1), bBottom + new Vector2(1,1), GetOutlineColor(), 0.0f);
                if (useHealthGradient) p_drawList.AddRectFilledMultiColor(bTop, bBottom, hCol2, horiz ? hCol1 : hCol2, horiz ? hCol2 : hCol1, hCol1);
                else p_drawList.AddRectFilled(bTop, bBottom, hCol1);
                if (draggingHealthBar) p_drawList.AddRect(bTop, bBottom, ImGui.ColorConvertFloat4ToU32(accentColor), 0, ImDrawFlags.None, 2.0f);

                if (showHealthText) {
                    string hpTxt = "100 HP"; Vector2 hpSize = ImGui.CalcTextSize(hpTxt); Vector2 hpPos;
                    bool _isH; hpPos = GetMockStackedPos(healthTextPos, hpSize, out _isH);
                    
                    if (draggingHPText) hpPos = mousePos - dragHPTOffset;
                    Rect hpHitbox = new Rect(hpPos - new Vector2(15,15), hpPos + hpSize + new Vector2(15,15));
                    if (isMouseDown && hpHitbox.Contains(mousePos) && !draggingHealthBar && !draggingDistance && !draggingHPText) {
                        draggingHPText = true; dragHPTOffset = mousePos - hpPos;
                    }
                    if (!isMouseDown && draggingHPText) {
                        for (int i = 0; i < 12; i++) if (snapZones[i].Contains(mousePos)) healthTextPos = (ESPPosition)i;
                        draggingHPText = false; SaveSettings();
                    }
                    p_drawList.AddText(hpPos, 0xFFFFFFFF, hpTxt);
                    if (draggingHPText) p_drawList.AddRect(hpPos, hpPos + hpSize, ImGui.ColorConvertFloat4ToU32(accentColor));
                }
            }

            if (showDistance) {
                string dst = "25.0m"; Vector2 dSize = ImGui.CalcTextSize(dst); Vector2 dPos;
                bool _isH; dPos = GetMockStackedPos(distancePos, dSize, out _isH);
                
                if (draggingDistance) dPos = mousePos - dragDistOffset;
                Rect dHitbox = new Rect(dPos - new Vector2(15,15), dPos + dSize + new Vector2(15,15));
                if (isMouseDown && dHitbox.Contains(mousePos) && !draggingHealthBar && !draggingHPText && !draggingDistance) {
                    draggingDistance = true; dragDistOffset = mousePos - dPos;
                }
                if (!isMouseDown && draggingDistance) {
                    for (int i = 0; i < 12; i++) if (snapZones[i].Contains(mousePos)) distancePos = (ESPPosition)i;
                    draggingDistance = false; SaveSettings();
                }
                p_drawList.AddText(dPos, 0xFFAAAAAA, dst);
                if (draggingDistance) p_drawList.AddRect(dPos, dPos + dSize, ImGui.ColorConvertFloat4ToU32(accentColor));
            }


            if (showSkeleton) {
                Vector4 sCol1 = skeletonColor;
                Vector4 sCol2 = skeletonColor2;
                bool sGrad = useSkeletonGradient;

                uint finalSCol1 = ImGui.ColorConvertFloat4ToU32(sCol1);
                uint finalSCol2 = ImGui.ColorConvertFloat4ToU32(new Vector4(sCol2.X, sCol2.Y, sCol2.Z, sCol1.W));
                uint sC = sGrad ? finalSCol2 : finalSCol1;
                float headRadius = 7.0f;
                Vector2 neck = headPos + new Vector2(0, headRadius * 1.5f);
                Vector2 pelvis = footPos - new Vector2(0, p_height * 0.48f); 

                if (showSkeletonGlow && enableGlow) {
                    void DrawSkelGlowLine(Vector2 p1, Vector2 p2) {
                        for (int i = 1; i <= 10; i++) {
                            float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                            uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(sCol1.X, sCol1.Y, sCol1.Z, 0.12f * alphaDecay));
                            float spread = (glowIntensity * i) * 0.45f;
                            p_drawList.AddLine(p1, p2, gCol, 1.0f + spread);
                        }
                    }
                    if (showHeadDot) {
                         for (int i = 1; i <= 10; i++) {
                            float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                            uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(sCol1.X, sCol1.Y, sCol1.Z, 0.12f * alphaDecay));
                            float spread = (glowIntensity * i) * 0.45f;
                            p_drawList.AddCircle(headPos + new Vector2(0, headRadius), headRadius + (spread / 2), gCol, 32, 1.0f + (spread / 2));
                        }
                    }
                    DrawSkelGlowLine(neck, pelvis);
                    DrawSkelGlowLine(neck, neck + new Vector2(-28, 20)); DrawSkelGlowLine(neck + new Vector2(-28, 20), neck + new Vector2(-35, 45));
                    DrawSkelGlowLine(neck, neck + new Vector2(28, 20)); DrawSkelGlowLine(neck + new Vector2(28, 20), neck + new Vector2(35, 45));
                    DrawSkelGlowLine(pelvis, pelvis + new Vector2(-15, 25)); DrawSkelGlowLine(pelvis + new Vector2(-15, 25), footPos - new Vector2(28, 0));
                    DrawSkelGlowLine(pelvis, pelvis + new Vector2(15, 25)); DrawSkelGlowLine(pelvis + new Vector2(15, 25), footPos + new Vector2(28, 0));
                }

                if (enableOutlines && showSkeletonOutline) {
                    if (showHeadDot) p_drawList.AddCircle(headPos + new Vector2(0, headRadius), headRadius + 1.5f, GetOutlineColor(), 32, 3.5f);
                    p_drawList.AddLine(neck, pelvis, GetOutlineColor(), 3.5f);
                    p_drawList.AddLine(neck, neck + new Vector2(-28, 20), GetOutlineColor(), 3.5f); p_drawList.AddLine(neck + new Vector2(-28, 20), neck + new Vector2(-35, 45), GetOutlineColor(), 3.5f);
                    p_drawList.AddLine(neck, neck + new Vector2(28, 20), GetOutlineColor(), 3.5f); p_drawList.AddLine(neck + new Vector2(28, 20), neck + new Vector2(35, 45), GetOutlineColor(), 3.5f);
                    p_drawList.AddLine(pelvis, pelvis + new Vector2(-15, 25), GetOutlineColor(), 3.5f); p_drawList.AddLine(pelvis + new Vector2(-15, 25), footPos - new Vector2(28, 0), GetOutlineColor(), 3.5f);
                    p_drawList.AddLine(pelvis, pelvis + new Vector2(15, 25), GetOutlineColor(), 3.5f); p_drawList.AddLine(pelvis + new Vector2(15, 25), footPos + new Vector2(28, 0), GetOutlineColor(), 3.5f);
                }

                if (showHeadDot) p_drawList.AddCircle(headPos + new Vector2(0, headRadius), headRadius, sC, 32, 1.5f);
                p_drawList.AddLine(neck, pelvis, sC, 1.5f);
                p_drawList.AddLine(neck, neck + new Vector2(-28, 20), sC, 1.5f); p_drawList.AddLine(neck + new Vector2(-28, 20), neck + new Vector2(-35, 45), sC, 1.5f);
                p_drawList.AddLine(neck, neck + new Vector2(28, 20), sC, 1.5f); p_drawList.AddLine(neck + new Vector2(28, 20), neck + new Vector2(35, 45), sC, 1.5f);
                p_drawList.AddLine(pelvis, pelvis + new Vector2(-15, 25), sC, 1.5f); p_drawList.AddLine(pelvis + new Vector2(-15, 25), footPos - new Vector2(28, 0), sC, 1.5f);
                p_drawList.AddLine(pelvis, pelvis + new Vector2(15, 25), sC, 1.5f); p_drawList.AddLine(pelvis + new Vector2(15, 25), footPos + new Vector2(28, 0), sC, 1.5f);
            }
            ImGui.EndChild();
        }

        private void DrawFOVPreview()
        {
            ImGui.BeginChild("FOVPreview", new Vector2(0, 0), ImGuiChildFlags.None);
            var p_windowPos = ImGui.GetWindowPos(); var p_windowSize = ImGui.GetWindowSize(); var p_drawList = ImGui.GetWindowDrawList();
            // Removed grey background box
            Vector2 center = p_windowPos + (p_windowSize / 2);
            float visualFov = (aimFov / 500f) * (p_windowSize.Y * 0.4f) + 10;
            uint uFov = ImGui.ColorConvertFloat4ToU32(new Vector4(fovColor.X, fovColor.Y, fovColor.Z, fovOpacity));
            uint uOutline = GetOutlineColor();

            if (enableOutlines && showFovOutline) { p_drawList.AddCircle(center, visualFov + 1, uOutline, 64, 2.5f); p_drawList.AddCircle(center, visualFov - 1, uOutline, 64, 1.0f); }
            if (useFovGradient) {
                uint uFov2 = ImGui.ColorConvertFloat4ToU32(fovColor2);
                for (int i = 0; i < 64; i++) {
                    float a1 = (float)(i * (Math.PI * 2) / 64); float a2 = (float)((i + 1) * (Math.PI * 2) / 64);
                    p_drawList.AddLine(center + new Vector2((float)Math.Cos(a1)*visualFov, (float)Math.Sin(a1)*visualFov), center + new Vector2((float)Math.Cos(a2)*visualFov, (float)Math.Sin(a2)*visualFov), Vector4.Lerp(new Vector4(fovColor.X, fovColor.Y, fovColor.Z, 1), new Vector4(fovColor2.X, fovColor2.Y, fovColor2.Z, 1), i/64.0f).ToU32(), 2.0f);
                }
            } else p_drawList.AddCircle(center, visualFov, uFov, 64, 1.5f);
            ImGui.EndChild();
        }

        private void DrawSkeleton(Entity entity)
        {
            if (entity.bones2D == null || entity.bones2D.Count == 0) return;
            int[][] bonePairs = { new int[] { 6, 5 }, new int[] { 5, 4 }, new int[] { 4, 0 }, new int[] { 5, 8 }, new int[] { 8, 9 }, new int[] { 9, 11 }, new int[] { 5, 13 }, new int[] { 13, 14 }, new int[] { 14, 16 }, new int[] { 0, 23 }, new int[] { 23, 24 }, new int[] { 0, 26 }, new int[] { 26, 27 } };
            Vector4 sCol1 = entity.isTeammate ? teamSkeletonColor : skeletonColor;
            Vector4 sCol2 = entity.isTeammate ? teamSkeletonColor2 : skeletonColor2;
            bool sGrad = entity.isTeammate ? useTeamSkeletonGradient : useSkeletonGradient;
            uint uC1 = ImGui.ColorConvertFloat4ToU32(sCol1);
            uint uC2 = ImGui.ColorConvertFloat4ToU32(sCol2);
            uint uOutline = GetOutlineColor();

            foreach (var pair in bonePairs)
            {
                if (entity.bones2D.ContainsKey(pair[0]) && entity.bones2D.ContainsKey(pair[1]))
                {
                    Vector2 p1 = entity.bones2D[pair[0]]; Vector2 p2 = entity.bones2D[pair[1]];
                    if (showSkeletonGlow && enableGlow) {
                        for (int i = 1; i <= 10; i++)
                        {
                            float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                            uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(sCol1.X, sCol1.Y, sCol1.Z, 0.12f * alphaDecay));
                            float spread = (glowIntensity * i) * 0.45f;
                            drawList.AddLine(p1, p2, gCol, 1.0f + spread);
                        }
                    }
                    if (enableOutlines && showSkeletonOutline) drawList.AddLine(p1, p2, uOutline, 3.5f);
                    drawList.AddLine(p1, p2, sGrad ? uC2 : uC1, 1.5f);
                }
            }
        }

        private string GetKeyName(int vKey)
        {
            if (vKey >= 0x41 && vKey <= 0x5A) return ((char)vKey).ToString();
            if (vKey >= 0x30 && vKey <= 0x39) return ((char)vKey).ToString();

            switch (vKey)
            {
                case 0x01: return "LBUTTON";
                case 0x02: return "RBUTTON";
                case 0x04: return "MBUTTON";
                case 0x05: return "XBUTTON1";
                case 0x06: return "XBUTTON2";
                case 0x12: return "ALT";
                case 0xA4: return "LALT";
                case 0xA5: return "RALT";
                case 0x1B: return "ESCAPE";
                case 0x20: return "SPACE";
                case 0xA0: return "LSHIFT";
                case 0xA1: return "RSHIFT";
                case 0xA2: return "LCONTROL";
                case 0xA3: return "RCONTROL";
                case 0x2D: return "INSERT";
                case 0x2E: return "DELETE";
                case 0x70: return "F1";
                case 0x71: return "F2";
                case 0x72: return "F3";
                case 0x73: return "F4";
                case 0x74: return "F5";
                case 0x75: return "F6";
                case 0x76: return "F7";
                case 0x77: return "F8";
                case 0x78: return "F9";
                case 0x79: return "F10";
                case 0x7A: return "F11";
                case 0x7B: return "F12";
                default: return "0x" + vKey.ToString("X");
            }
        }

        private unsafe void DrawWatermark()
        {
            if (!showWatermark) return;

            string time = DateTime.Now.ToString("HH:mm:ss");
            float fps = ImGui.GetIO().Framerate;
            string text = $"Snow | {userName} | {time} | {fps:0} FPS";
            
            Vector2 padding = new Vector2(10, 8);
            Vector2 textSize = ImGui.CalcTextSize(text);
            Vector2 boxSize = textSize + (padding * 2);
            Vector2 boxPos = new Vector2(20, 20);

            // Background boxes removed for cleaner look
            drawList.AddRectFilled(boxPos + new Vector2(0, boxSize.Y - 2), boxPos + boxSize, ImGui.ColorConvertFloat4ToU32(accentColor), 2.0f, ImDrawFlags.RoundCornersBottom);

            Vector2 textPos = boxPos + padding;
            string brand = "Snow";
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(accentColor), brand);
            string rest = text.Substring(brand.Length);
            drawList.AddText(textPos + new Vector2(ImGui.CalcTextSize(brand).X, 0), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), rest);
        }

        private void DrawOverlay()
        {
            ImGui.SetNextWindowSize(screenSize);
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.Begin("Overlay", ImGuiWindowFlags.NoDecoration
               | ImGuiWindowFlags.NoBackground
               | ImGuiWindowFlags.NoBringToFrontOnFocus
               | ImGuiWindowFlags.NoMove
               | ImGuiWindowFlags.NoInputs
               | ImGuiWindowFlags.NoCollapse
               | ImGuiWindowFlags.NoScrollbar
               | ImGuiWindowFlags.NoScrollWithMouse
               );

            drawList = ImGui.GetWindowDrawList(); 
            DrawWatermark();
            DrawBombTimer();
            InitOverlaySnowflakes(); if (espSnowflakes) UpdateOverlaySnowflakes();

            if (showFovCircle) {
                uint fovCol1 = ImGui.ColorConvertFloat4ToU32(fovColor); uint fovCol2 = ImGui.ColorConvertFloat4ToU32(fovColor2);
                uint uBlack = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, fovOpacity));
                if (enableOutlines && showFovOutline) { drawList.AddCircle(new Vector2(screenSize.X / 2, screenSize.Y / 2), aimFov + 1.5f, uBlack, 100, 2.0f); drawList.AddCircle(new Vector2(screenSize.X / 2, screenSize.Y / 2), aimFov - 1.5f, uBlack, 100, 2.0f); }
                if (useFovGradient) {
                    for (int i = 0; i < 64; i++) {
                        float a1 = (float)(i * (Math.PI * 2) / 64); float a2 = (float)((i + 1) * (Math.PI * 2) / 64);
                        Vector2 p1 = new Vector2(screenSize.X/2 + (float)Math.Cos(a1)*aimFov, screenSize.Y/2 + (float)Math.Sin(a1)*aimFov);
                        Vector2 p2 = new Vector2(screenSize.X/2 + (float)Math.Cos(a2)*aimFov, screenSize.Y/2 + (float)Math.Sin(a2)*aimFov);
                        drawList.AddLine(p1, p2, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(fovColor, fovColor2, i/64.0f)), 2.0f);
                    }
                } else drawList.AddCircle(new Vector2(screenSize.X / 2, screenSize.Y / 2), aimFov, fovCol1, 100, 2.0f);
            }

            if (enableESP) {
                foreach (var entity in entities)
                {
                    if (entity.position2D.X > 0 && entity.position2D.Y > 0 && entity.position2D.X != -999)
                    {
                        if (!showTeam && entity.isTeammate) continue;
                        Vector4 bCol = entity.isTeammate ? teamBoxColor : boxColor;
                        Vector4 bCol2 = entity.isTeammate ? teamBoxColor2 : boxColor2;
                        bool bGrad = entity.isTeammate ? useTeamBoxGradient : useBoxGradient;

                        float[] offsets = new float[12];
                        if (showHealthBar) DrawHealthBar(entity, ref offsets);
                        if (showSkeleton) DrawSkeleton(entity);
                        if (showBox) DrawBox(entity, bCol, bCol2, bGrad);
                        DrawTactical(entity, ref offsets);
                        if (showLines) DrawLine(entity);
                        if (showHeadDot) DrawHeadDot(entity);
                    }
                }
            }
            ImGui.End();
        }



        private unsafe void DrawHealthBar(Entity entity, ref float[] offsets)
        {
            float h = entity.position2D.Y - entity.viewPosition2D.Y;
            float w = Math.Abs(h) / 1.5f;
            float barThickness = Math.Clamp(Math.Abs(h) / 50.0f, 2.0f, 4.5f);

            Vector2 rectTop = new Vector2(entity.viewPosition2D.X - w / 2, entity.viewPosition2D.Y);
            Vector2 rectBottom = new Vector2(entity.viewPosition2D.X + w / 2, entity.position2D.Y);

            Vector2 barTop = Vector2.Zero, barBottom = Vector2.Zero;
            bool horizontal = (int)healthBarPos >= 6;

            Vector2 hSize = horizontal ? new Vector2(w, barThickness) : new Vector2(barThickness, Math.Abs(h));
            barTop = GetStackedPos(healthBarPos, hSize, rectTop, rectBottom, w, Math.Abs(h), ref offsets[(int)healthBarPos]);
            barBottom = barTop + hSize;

            uint hCol1 = ImGui.ColorConvertFloat4ToU32(new Vector4(healthBarColor.X, healthBarColor.Y, healthBarColor.Z, healthBarOpacity));
            uint hCol2 = ImGui.ColorConvertFloat4ToU32(new Vector4(healthBarColor2.X, healthBarColor2.Y, healthBarColor2.Z, healthBarOpacity));
            
            // Draw background
            if (enableOutlines && showHealthOutline) drawList.AddRectFilled(barTop - new Vector2(1, 1), barBottom + new Vector2(1, 1), GetOutlineColor(), 0.0f);

            // Calculate current health bar rect
            Vector2 currentBarTop = barTop;
            Vector2 currentBarBottom = barBottom;
            float pct = entity.health / 100f;
            if (horizontal) currentBarBottom.X = barTop.X + (barBottom.X - barTop.X) * pct;
            else currentBarTop.Y = barBottom.Y - (barBottom.Y - barTop.Y) * pct;

            if (showHealthGlow && enableGlow)
            {
                for (int i = 1; i <= 10; i++)
                {
                    float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                    uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(healthBarColor.X, healthBarColor.Y, healthBarColor.Z, 0.15f * alphaDecay));
                    float spread = (glowIntensity * i) * 0.3f;
                    drawList.AddRectFilled(currentBarTop - new Vector2(spread, spread), currentBarBottom + new Vector2(spread, spread), gCol, 0.0f);
                }
            }
            
            if (useHealthGradient) drawList.AddRectFilledMultiColor(currentBarTop, currentBarBottom, hCol2, horizontal ? hCol1 : hCol2, horizontal ? hCol2 : hCol1, hCol1);
            else drawList.AddRectFilled(currentBarTop, currentBarBottom, hCol1, 0.0f);

            // HP Text
            if (showHealthText)
            {
                string hpText = $"{entity.health} HP";
                Vector2 textSize = ImGui.CalcTextSize(hpText);
                Vector2 textPos = GetStackedPos(healthTextPos, textSize, rectTop, rectBottom, w, Math.Abs(h), ref offsets[(int)healthTextPos]);
                drawList.AddText(textPos, 0xFFFFFFFF, hpText);
            }
        }

        private unsafe void DrawBombTimer()
        {
            if (!showBombTimer || bombPawn == IntPtr.Zero || swed == null) return;

            float timerLength = swed.ReadFloat(bombPawn, OffsetManager.Offsets.m_flTimerLength);
            float bombTick = swed.ReadFloat(bombPawn, OffsetManager.Offsets.m_flBombTickTime);
            
            float timeLeft = bombTick;
            if (timeLeft <= 0 || timeLeft > 45) return;

            float progress = timeLeft / 40.0f;
            Vector2 barSize = new Vector2(400, 10);
            Vector2 barPos = new Vector2(screenSize.X / 2 - barSize.X / 2, 80);

            drawList.AddRectFilled(barPos + new Vector2(2, 2), barPos + barSize + new Vector2(2, 2), ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.4f)), 4.0f);
            drawList.AddRectFilled(barPos, barPos + barSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 0.8f)), 4.0f);
            Vector4 col = timeLeft < 10 ? new Vector4(1, 0, 0, 1) : accentColor;
            drawList.AddRectFilled(barPos, barPos + new Vector2(barSize.X * progress, barSize.Y), ImGui.ColorConvertFloat4ToU32(col), 4.0f);
            
            string timeText = $"BOMB: {timeLeft:F1}s";
            Vector2 txtSize = ImGui.CalcTextSize(timeText);
            drawList.AddText(new Vector2(screenSize.X / 2 - txtSize.X / 2, barPos.Y - txtSize.Y - 5), 0xFFFFFFFF, timeText);
        }

        private unsafe void DrawBox(Entity entity, Vector4 customColor, Vector4 gradColor, bool useGrad)
        {
            float h = entity.position2D.Y - entity.viewPosition2D.Y;
            Vector2 rectTop = new Vector2(entity.viewPosition2D.X - h / 3, entity.viewPosition2D.Y);
            Vector2 rectBottom = new Vector2(entity.viewPosition2D.X + h / 3, entity.position2D.Y);

            uint uC1 = ImGui.ColorConvertFloat4ToU32(customColor);
            uint uOutline = GetOutlineColor();

            if (showBoxGlow && enableGlow)
            {
                for (int i = 1; i <= 10; i++)
                {
                    float alphaDecay = (float)Math.Pow(1.1f - (i / 10.0f), 3);
                    uint gCol = ImGui.ColorConvertFloat4ToU32(new Vector4(customColor.X, customColor.Y, customColor.Z, 0.15f * alphaDecay));
                    float spread = (glowIntensity * i) * 0.4f;
                    drawList.AddRect(rectTop - new Vector2(spread, spread), rectBottom + new Vector2(spread, spread), gCol, 4.0f, ImDrawFlags.None, 1.0f + spread);
                }
            }
            if (useSplitBox) {
                float lW = (rectBottom.X - rectTop.X) / 4; float lH = (rectBottom.Y - rectTop.Y) / 4;
                void DB(Vector2 p1, Vector2 p2) { if (enableOutlines && showBoxOutline) drawList.AddLine(p1, p2, uOutline, 3.5f); drawList.AddLine(p1, p2, uC1, 2.0f); }
                DB(rectTop, new Vector2(rectTop.X + lW, rectTop.Y)); DB(rectTop, new Vector2(rectTop.X, rectTop.Y + lH));
                DB(new Vector2(rectBottom.X, rectTop.Y), new Vector2(rectBottom.X - lW, rectTop.Y)); DB(new Vector2(rectBottom.X, rectTop.Y), new Vector2(rectBottom.X, rectTop.Y + lH));
                DB(new Vector2(rectTop.X, rectBottom.Y), new Vector2(rectTop.X + lW, rectBottom.Y)); DB(new Vector2(rectTop.X, rectBottom.Y), new Vector2(rectTop.X, rectBottom.Y - lH));
                DB(rectBottom, new Vector2(rectBottom.X - lW, rectBottom.Y)); DB(rectBottom, new Vector2(rectBottom.X, rectBottom.Y - lH));
            } else {
                if (enableOutlines && showBoxOutline) drawList.AddRect(rectTop, rectBottom, uOutline, 4.0f, ImDrawFlags.None, 4.0f);
                if (useGrad) drawList.AddRectFilledMultiColor(rectTop, rectBottom, ImGui.ColorConvertFloat4ToU32(gradColor), ImGui.ColorConvertFloat4ToU32(gradColor), uC1, uC1);
                drawList.AddRect(rectTop, rectBottom, uC1, 4.0f, ImDrawFlags.None, 2.0f);
            }

            if (entity.isDefusing)
            {
                drawList.AddText(new Vector2(rectTop.X, rectTop.Y - 20), 0xFF00AAFF, "DEFUSING");
            }
        }

        private void DrawTactical(Entity entity, ref float[] offsets)
        {
            if (!showDistance) return;

            float h = entity.position2D.Y - entity.viewPosition2D.Y;
            float w = Math.Abs(h) / 1.5f;

            Vector2 rectTop = new Vector2(entity.viewPosition2D.X - w / 2, entity.viewPosition2D.Y);
            Vector2 rectBottom = new Vector2(entity.viewPosition2D.X + w / 2, entity.position2D.Y);

            string distText = $"{(int)entity.distance}m";
            Vector2 textSize = ImGui.CalcTextSize(distText);
            Vector2 textPos = GetStackedPos(distancePos, textSize, rectTop, rectBottom, w, Math.Abs(h), ref offsets[(int)distancePos]);
            drawList.AddText(textPos, 0xFFFFFFFF, distText);
        }


        private void DrawHeadDot(Entity entity) { drawList.AddCircleFilled(entity.viewPosition2D, 4.0f, ImGui.ColorConvertFloat4ToU32(new Vector4(headDotColor.X, headDotColor.Y, headDotColor.Z, headDotOpacity))); }
        private void DrawLine(Entity entity) { drawList.AddLine(new Vector2(screenSize.X / 2, screenSize.Y), entity.position2D, ImGui.ColorConvertFloat4ToU32(new Vector4(lineColor.X, lineColor.Y, lineColor.Z, boxOpacity)), 2.0f); }
        public void UpdateUsage(float minutes) { totalTimeMinutes += minutes; }
        public void UpdateEntities(IEnumerable<Entity> newentities) { entities = new ConcurrentQueue<Entity>(newentities); }
        public void UpdateLocalPlayer(Entity newEntity) { localPlayer = newEntity; }
        public Entity GetLocalPlayer() { return localPlayer; }
    }

    public static class Vector4Extensions {
        public static uint ToU32(this Vector4 v) => ImGui.ColorConvertFloat4ToU32(v);
    }
}
