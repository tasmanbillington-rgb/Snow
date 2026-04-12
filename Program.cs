using BasicESP;
using Swed64;
using System.Numerics;
using System.Runtime.InteropServices;

Renderer renderer = new Renderer();
Thread renderThread = new Thread(new ThreadStart(renderer.Start().Wait));
renderThread.Start();

Vector2 screenSize = new Vector2(1920, 1080);
renderer.screenSize = screenSize;
List<Entity> entities = new List<Entity>();
Entity localPlayer = new Entity();

Swed? swed = null;
IntPtr client = IntPtr.Zero;

Console.WriteLine("Waiting for CS2...");
while (true)
{
    try
    {
        swed = new Swed("cs2");
        client = swed.GetModuleBase("client.dll");
        if (client != IntPtr.Zero) break;
    }
    catch { /* Process or module not found yet */ }
    Thread.Sleep(1000);
}
Console.WriteLine("CS2 Found! Attaching...");

[DllImport("user32.dll")]
static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
[DllImport("user32.dll")]
static extern short GetAsyncKeyState(int vKey);

DateTime startTime = DateTime.Now;



while (true)
{
    var offsets = OffsetManager.Offsets;
    entities.Clear();

    IntPtr entityList = swed.ReadPointer(client, offsets.dwEntityList);
    IntPtr localPlayerPawn = swed.ReadPointer(client, offsets.dwLocalPlayerPawn);

    Vector3 localOrigin = Vector3.Zero;
    if (localPlayerPawn != IntPtr.Zero)
    {
        localPlayer.team = swed.ReadInt(localPlayerPawn, offsets.m_iTeamNum);
        localOrigin = swed.ReadVec(localPlayerPawn, offsets.m_vOldOrigin);
    }

    float[] viewMatrix = swed.ReadMatrix(client + offsets.dwViewMatrix);
    bool matrixValid = false;
    foreach (var f in viewMatrix) if (f != 0) { matrixValid = true; break; }
    
    // Robust ViewMatrix Fallback trace (sometimes it drifts or is zeroed on certain maps)
    if (!matrixValid)
    {
        // Try to finding it by brute force offset scan if zeroed (rare but happens on Inferno/Nuke)
        for (int offset = 0; offset < 0x1000; offset += 0x10)
        {
            float[] tempMatrix = swed.ReadMatrix(client + offsets.dwViewMatrix + offset);
            bool tempValid = false;
            foreach (var f in tempMatrix) if (f != 0) { tempValid = true; break; }
            if (tempValid) { viewMatrix = tempMatrix; break; }
        }
    }

    // Bomb Timer Logic
    IntPtr plantedC4 = swed.ReadPointer(client, offsets.dwPlantedC4);
    if (plantedC4 != IntPtr.Zero)
    {
        float bombTime = swed.ReadFloat(plantedC4, offsets.m_flBombTickTime);
        float currentTime = swed.ReadFloat(swed.GetModuleBase("engine2.dll"), 0x0); // Simulation: need actual engine time
        // Simplified bomb check: if plantedC4 exists, we'll handle the UI in Renderer helper
        Renderer.bombPawn = plantedC4;
    }
    else
    {
        Renderer.bombPawn = IntPtr.Zero;
    }

    for (int i = 0; i < 4096; i++)
    {
        IntPtr listEntry = swed.ReadPointer(entityList, (int)(0x70 * (i >> 9) + 0x10));
        if (listEntry == IntPtr.Zero) continue;

        IntPtr currentController = swed.ReadPointer(listEntry, (int)(0x70 * (i & 0x1FF)));
        if (currentController == IntPtr.Zero) continue;

        int pawnHandle = swed.ReadInt(currentController, offsets.m_hPlayerPawn);
        if (pawnHandle == 0) continue;

        IntPtr listEntry2 = swed.ReadPointer(entityList, (int)(0x70 * ((pawnHandle & 0x7FFF) >> 9) + 0x10));
        if (listEntry2 == IntPtr.Zero) continue;

        IntPtr currentPawn = swed.ReadPointer(listEntry2, (int)(0x70 * (pawnHandle & 0x1FF)));
        if (currentPawn == IntPtr.Zero) continue;

        byte lifeState = (byte)swed.ReadInt(currentPawn, offsets.m_lifeState);
        if (lifeState != 0) continue;

        int team = swed.ReadInt(currentPawn, offsets.m_iTeamNum);
        int health = swed.ReadInt(currentPawn, offsets.m_iHealth);

        if (team == 0 || health <= 0 || health > 100) continue;
        
        // Teammate check logic
        bool isTeammate = (team == localPlayer.team);
        if (isTeammate && !Renderer.showTeam) continue;

        Entity entity = new Entity();
        entity.team = team;
        entity.health = health;
        entity.isTeammate = isTeammate;
        entity.position = swed.ReadVec(currentPawn, offsets.m_vOldOrigin);
        entity.viewOffset = swed.ReadVec(currentPawn, offsets.m_vecViewOffset);
        entity.position2D = Calculate.WorldToScreen(viewMatrix, entity.position, screenSize);
        entity.viewPosition2D = Calculate.WorldToScreen(viewMatrix, Vector3.Add(entity.position, entity.viewOffset), screenSize);
        
        // Tactical Data
        entity.distance = Vector3.Distance(localOrigin, entity.position) / 39.37f; // Scale to meters (1m = ~39.37 units)
        entity.isDefusing = swed.ReadBool(currentPawn, offsets.m_bIsDefusing);

        // Skeleton Logic
        IntPtr sceneNode = swed.ReadPointer(currentPawn, offsets.m_pGameSceneNode);
        IntPtr boneArray = swed.ReadPointer(sceneNode, offsets.m_modelState + 0x80);

        if (boneArray != IntPtr.Zero)
        {
            int[] boneIds = { 6, 5, 4, 0, 8, 9, 11, 13, 14, 16, 23, 24, 26, 27 };
            foreach (int boneId in boneIds)
            {
                Vector3 bonePos = swed.ReadVec(boneArray, boneId * 32);
                Vector2 bonePos2D = Calculate.WorldToScreen(viewMatrix, bonePos, screenSize);
                entity.bones2D[boneId] = bonePos2D;
            }
        }

        entities.Add(entity);
    }

    if (Renderer.enableSoftAim)
    {
        short hotkeyState = GetAsyncKeyState(Renderer.softAimKey);
        if ((hotkeyState & 0x8000) != 0)
        {
            Entity? target = null;
            float minDistance = Renderer.aimFov;
            Vector2 screenCenter = new Vector2(screenSize.X / 2, screenSize.Y / 2);

            foreach (var entity in entities)
            {
                if (entity.viewPosition2D.X > 0 && entity.viewPosition2D.Y > 0 && entity.viewPosition2D.X != -999)
                {
                    float dist = Vector2.Distance(screenCenter, entity.viewPosition2D);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        target = entity;
                    }
                }
            }

            if (target != null)
            {
                float smooth = Renderer.aimSmoothness;
                if (smooth < 1.0f) smooth = 1.0f;

                float deltaX = target.viewPosition2D.X - screenCenter.X;
                float deltaY = target.viewPosition2D.Y - screenCenter.Y;

                float moveX = deltaX / smooth;
                float moveY = deltaY / smooth;

                if (Math.Abs(moveX) < 1.0f && Math.Abs(deltaX) > 0.1f) moveX = deltaX > 0 ? 1 : -1;
                if (Math.Abs(moveY) < 1.0f && Math.Abs(deltaY) > 0.1f) moveY = deltaY > 0 ? 1 : -1;

                mouse_event(0x0001, (int)moveX, (int)moveY, 0, 0);
            }
        }
    }

    renderer.UpdateEntities(entities);
    renderer.UpdateUsage(0.001f);
    renderer.swed = swed; // Share swed with renderer for bomb time calc

    Thread.Sleep(1);
}

