using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BasicESP
{
    public class Entity
    {
        public Vector3 position { get; set; }
        public Vector3 viewOffset { get; set; }
        public Vector2 position2D { get; set; }
        public Vector2 viewPosition2D { get; set; }
        public int team { get; set; }
        public int health { get; set; }
        public float distance { get; set; }
        public string weaponName { get; set; } = "Unknown";
        public bool isDefusing { get; set; }
        public bool isTeammate { get; set; }
        public Dictionary<int, Vector2> bones2D { get; set; } = new Dictionary<int, Vector2>();
    }
}
