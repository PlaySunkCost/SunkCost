using UnityEngine;

namespace SunkCost.Player
{
    // Two-segment arm: shoulder → elbow → wrist with fixed segment lengths and an
    // elbow hint (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 6).
    // Pure geometry: it never moves an item and never stretches; an
    // out-of-reach target is clamped and reported.
    public static class ArmPoseSolver
    {
        public struct Result
        {
            public Vector3 Elbow;
            public Vector3 Wrist;   // the reached wrist (clamped when the target is too far)
            public bool Clamped;
        }

        public static Result Solve(Vector3 shoulder, Vector3 wristTarget, Vector3 elbowHint, float upperLength, float forearmLength)
        {
            Vector3 toTarget = wristTarget - shoulder;
            float reach = upperLength + forearmLength;
            float distance = toTarget.magnitude;
            var result = new Result { Clamped = false };
            if (distance < 1e-4f)
            {
                result.Wrist = shoulder + Vector3.forward * (reach - 1e-3f);
                toTarget = result.Wrist - shoulder;
                distance = toTarget.magnitude;
            }
            float minDistance = Mathf.Abs(upperLength - forearmLength) + 1e-3f;
            if (distance > reach - 1e-3f) { distance = reach - 1e-3f; result.Clamped = true; }
            if (distance < minDistance) { distance = minDistance; result.Clamped = true; }
            Vector3 direction = toTarget.normalized;
            result.Wrist = shoulder + direction * distance;

            // Law of cosines for the elbow angle, bent toward the hint.
            float cosShoulder = Mathf.Clamp((upperLength * upperLength + distance * distance - forearmLength * forearmLength) / (2f * upperLength * distance), -1f, 1f);
            float shoulderAngle = Mathf.Acos(cosShoulder);
            Vector3 hint = elbowHint - Vector3.Project(elbowHint, direction);
            if (hint.sqrMagnitude < 1e-6f) hint = Vector3.Cross(direction, Vector3.up);
            if (hint.sqrMagnitude < 1e-6f) hint = Vector3.right;
            hint.Normalize();
            result.Elbow = shoulder + (direction * Mathf.Cos(shoulderAngle) + hint * Mathf.Sin(shoulderAngle)) * upperLength;
            return result;
        }
    }
}
