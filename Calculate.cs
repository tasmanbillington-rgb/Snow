using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BasicESP
{
    public static class Calculate
    {
        public static Vector2 WorldToScreen(float[]? matrix, Vector3 pos, Vector2 windowSize)
        {
            // Improved WorldToScreen with better validation
            if (matrix == null || matrix.Length < 16)
            {
                return new Vector2(-999, -999); // Invalid matrix
            }

            // Calculate screen coordinates
            float screenW = (matrix[12] * pos.X) + (matrix[13] * pos.Y) + (matrix[14] * pos.Z) + matrix[15];

            // Better validation for W component
            if (screenW < 0.001f)
            {
                return new Vector2(-999, -999); // Behind camera or too close
            }

            float screenX = (matrix[0] * pos.X) + (matrix[1] * pos.Y) + (matrix[2] * pos.Z) + matrix[3];
            float screenY = (matrix[4] * pos.X) + (matrix[5] * pos.Y) + (matrix[6] * pos.Z) + matrix[7];

            // Normalize by W
            float normalizedX = screenX / screenW;
            float normalizedY = screenY / screenW;

            // Convert to screen coordinates
            float X = (windowSize.X * 0.5f) + (normalizedX * windowSize.X * 0.5f);
            float Y = (windowSize.Y * 0.5f) - (normalizedY * windowSize.Y * 0.5f);

            // Validate final coordinates
            if (float.IsNaN(X) || float.IsNaN(Y) || float.IsInfinity(X) || float.IsInfinity(Y))
            {
                return new Vector2(-999, -999);
            }

            return new Vector2(X, Y);
        }
    }
}