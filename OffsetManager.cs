using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BasicESP
{
    public class OffsetManager
    {
        private const string ConfigPath = "offsets_config.json";
        private const string OffsetsUrl = "https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/offsets.json";
        private const string ClientDllUrl = "https://raw.githubusercontent.com/a2x/cs2-dumper/main/output/client_dll.json";

        public static OffsetData Offsets { get; private set; } = new OffsetData();

        public static void Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    Offsets = JsonSerializer.Deserialize<OffsetData>(json) ?? new OffsetData();
                }
                catch { InitDefaults(); }
            }
            else
            {
                InitDefaults();
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(Offsets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        private static void InitDefaults()
        {
            Offsets = new OffsetData
            {
                dwEntityList = 0x24B3268,
                dwViewMatrix = 0x2313F10,
                dwLocalPlayerPawn = 0x206D9E0,
                m_vOldOrigin = 0x1588,
                m_iTeamNum = 0x3F3,
                m_lifeState = 0x35C,
                m_hPlayerPawn = 0x90C,
                m_vecViewOffset = 0xD58,
                m_iHealth = 0x354,
                m_pGameSceneNode = 0x338,
                m_modelState = 0x160,
                dwPlantedC4 = 0x0, // Will be fetched
                m_pClippingWeapon = 0x1308,
                m_bIsDefusing = 0x1408,
                m_flTimerLength = 0x3D0,
                m_flBombTickTime = 0x3D8,
                m_iItemDefinitionIndex = 0x1BA // Offset within weapon entity
            };
            Save();
        }

        public static async Task<(bool success, string message, string detail)> UpdateFromGitHub()
        {
            var successFields = new List<string>();
            var failedFields = new List<string>();
            
            try
            {
                using HttpClient client = new HttpClient();
                
                string offsetsJsonStr = await client.GetStringAsync(OffsetsUrl);
                using JsonDocument offsetsDoc = JsonDocument.Parse(offsetsJsonStr);
                JsonElement clientDllOffsets = offsetsDoc.RootElement.GetProperty("client.dll");

                void AddO(string name, bool success) { if (success) successFields.Add(name); else failedFields.Add(name); }

                // Mandatory offsets
                try { Offsets.dwEntityList = clientDllOffsets.GetProperty("dwEntityList").GetInt32(); AddO("dwEntityList", true); } catch { AddO("dwEntityList", false); }
                try { Offsets.dwViewMatrix = clientDllOffsets.GetProperty("dwViewMatrix").GetInt32(); AddO("dwViewMatrix", true); } catch { AddO("dwViewMatrix", false); }
                try { Offsets.dwLocalPlayerPawn = clientDllOffsets.GetProperty("dwLocalPlayerPawn").GetInt32(); AddO("dwLocalPlayerPawn", true); } catch { AddO("dwLocalPlayerPawn", false); }
                
                // Optional/Tactical offsets with safety
                if (clientDllOffsets.TryGetProperty("dwPlantedC4", out var bPtr)) { Offsets.dwPlantedC4 = bPtr.GetInt32(); AddO("dwPlantedC4", true); } else AddO("dwPlantedC4", false);

                string clientDllStr = await client.GetStringAsync(ClientDllUrl);
                using JsonDocument clientDllDoc = JsonDocument.Parse(clientDllStr);
                JsonElement classes = clientDllDoc.RootElement.GetProperty("client.dll").GetProperty("classes");

                // Helper to find class by multiple names
                JsonElement? GetClass(params string[] names) {
                    foreach (var n in names) if (classes.TryGetProperty(n, out var c)) return c;
                    return null;
                }

                var baseEntity = GetClass("C_BaseEntity");
                if (baseEntity.HasValue) {
                    var f = baseEntity.Value.GetProperty("fields");
                    try { Offsets.m_iTeamNum = f.GetProperty("m_iTeamNum").GetInt32(); AddO("m_iTeamNum", true); } catch { AddO("m_iTeamNum", false); }
                    try { Offsets.m_iHealth = f.GetProperty("m_iHealth").GetInt32(); AddO("m_iHealth", true); } catch { AddO("m_iHealth", false); }
                    try { Offsets.m_lifeState = f.GetProperty("m_lifeState").GetInt32(); AddO("m_lifeState", true); } catch { AddO("m_lifeState", false); }
                    try { Offsets.m_pGameSceneNode = f.GetProperty("m_pGameSceneNode").GetInt32(); AddO("m_pGameSceneNode", true); } catch { AddO("m_pGameSceneNode", false); }
                } else failedFields.Add("C_BaseEntity");

                var controller = GetClass("CBasePlayerController");
                if (controller.HasValue) {
                    try { Offsets.m_hPlayerPawn = controller.Value.GetProperty("fields").GetProperty("m_hPawn").GetInt32(); AddO("m_hPawn", true); } catch { AddO("m_hPawn", false); }
                } else failedFields.Add("CBasePlayerController");

                var pawn = GetClass("C_BasePlayerPawn", "C_CSPlayerPawn", "C_CSPlayerPawnBase");
                if (pawn.HasValue) {
                    try { Offsets.m_vOldOrigin = pawn.Value.GetProperty("fields").GetProperty("m_vOldOrigin").GetInt32(); AddO("m_vOldOrigin", true); } catch { AddO("m_vOldOrigin", false); }
                } else failedFields.Add("PlayerPawn");

                var modelEntity = GetClass("C_BaseModelEntity");
                if (modelEntity.HasValue) {
                    try { Offsets.m_vecViewOffset = modelEntity.Value.GetProperty("fields").GetProperty("m_vecViewOffset").GetInt32(); AddO("m_vecViewOffset", true); } catch { AddO("m_vecViewOffset", false); }
                } else failedFields.Add("C_BaseModelEntity");

                var skeleton = GetClass("CSkeletonInstance");
                if (skeleton.HasValue) {
                    try { Offsets.m_modelState = skeleton.Value.GetProperty("fields").GetProperty("m_modelState").GetInt32(); AddO("m_modelState", true); } catch { AddO("m_modelState", false); }
                } else failedFields.Add("CSkeletonInstance");
                
                // Tactical fields (Defensive)
                var csPawn = GetClass("C_CSPlayerPawn", "C_CSPlayerPawnBase");
                if (csPawn.HasValue) {
                    var f = csPawn.Value.GetProperty("fields");
                    if (f.TryGetProperty("m_pClippingWeapon", out var w)) { Offsets.m_pClippingWeapon = w.GetInt32(); AddO("m_pClippingWeapon", true); } else AddO("m_pClippingWeapon", false);
                    if (f.TryGetProperty("m_bIsDefusing", out var d)) { Offsets.m_bIsDefusing = d.GetInt32(); AddO("m_bIsDefusing", true); } else AddO("m_bIsDefusing", false);
                }
                
                if (classes.TryGetProperty("C_PlantedC4", out var bombCls)) {
                    var f = bombCls.GetProperty("fields");
                    if (f.TryGetProperty("m_flTimerLength", out var tl)) { Offsets.m_flTimerLength = tl.GetInt32(); AddO("m_flTimerLength", true); }
                    if (f.TryGetProperty("m_flBombTickTime", out var tt)) { Offsets.m_flBombTickTime = tt.GetInt32(); AddO("m_flBombTickTime", true); }
                } else failedFields.Add("C_PlantedC4");
                
                if (classes.TryGetProperty("C_EconItemView", out var econCls)) {
                    if (econCls.GetProperty("fields").TryGetProperty("m_iItemDefinitionIndex", out var idi)) { Offsets.m_iItemDefinitionIndex = idi.GetInt32(); AddO("m_iItemDefinitionIndex", true); }
                } else failedFields.Add("C_EconItemView");

                var weaponServices = GetClass("CPlayer_WeaponServices");
                if (weaponServices.HasValue) {
                    if (weaponServices.Value.GetProperty("fields").TryGetProperty("m_hActiveWeapon", out var haw)) { Offsets.m_hActiveWeapon = haw.GetInt32(); AddO("m_hActiveWeapon", true); }
                }

                if (pawn.HasValue) {
                    if (pawn.Value.GetProperty("fields").TryGetProperty("m_pWeaponServices", out var pws)) { Offsets.m_pWeaponServices = pws.GetInt32(); AddO("m_pWeaponServices", true); }
                }

                Save();
                string detail = $"Updated: {string.Join(", ", successFields)}\nMissing: {string.Join(", ", failedFields)}";
                return (true, "Update completed!", detail);
            }
            catch (Exception ex)
            {
                return (false, $"Update failed: {ex.Message}", "");
            }
        }


    }

    public class OffsetData
    {
        public int dwEntityList { get; set; }
        public int dwViewMatrix { get; set; }
        public int dwLocalPlayerPawn { get; set; }
        public int dwPlantedC4 { get; set; }
        public int m_vOldOrigin { get; set; }
        public int m_iTeamNum { get; set; }
        public int m_lifeState { get; set; }
        public int m_hPlayerPawn { get; set; }
        public int m_vecViewOffset { get; set; }
        public int m_iHealth { get; set; }
        public int m_pGameSceneNode { get; set; }
        public int m_modelState { get; set; }
        public int m_pClippingWeapon { get; set; }
        public int m_bIsDefusing { get; set; }
        public int m_flTimerLength { get; set; }
        public int m_flBombTickTime { get; set; }
        public int m_iItemDefinitionIndex { get; set; }
        public int m_pWeaponServices { get; set; }
        public int m_hActiveWeapon { get; set; }
    }

}
