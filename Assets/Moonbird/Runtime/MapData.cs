using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moonbird
{
    [Serializable]
    public sealed class JumpSettings
    {
        public float sensitivity = 1f;
        public float maxSpeed = 18f;
        public float gravity = 30f;
        public float maxAngle = 65f;
        public float minStrength = 0f;
    }

    [Serializable]
    public sealed class PlatformData
    {
        public string id;
        public Vector2 position;
        public Vector2 size;

        public PlatformData() { }
        public PlatformData(Vector2 position, Vector2 size)
        {
            id = Guid.NewGuid().ToString("N");
            this.position = position;
            this.size = size;
        }
    }

    [Serializable]
    public sealed class MapDocument
    {
        public int formatVersion = 1;
        public string name = "처음 달에 가는 날";
        public Vector2 spawn = new Vector2(0, .62f);
        public Vector2 goal = new Vector2(2.5f, 19);
        public float killY = -8;
        public JumpSettings jump = new JumpSettings();
        public List<PlatformData> platforms = new List<PlatformData>();

        public static MapDocument CreateDefault()
        {
            var map = new MapDocument();
            map.platforms.Add(new PlatformData(new Vector2(0, -.5f), new Vector2(16, 1)));
            for (var i = 1; i <= 8; i++)
                map.platforms.Add(new PlatformData(new Vector2(i % 2 == 0 ? 2.5f : -2.5f, i * 2),
                    new Vector2(i <= 3 ? 3.2f : 3, .45f)));
            return map;
        }

        public MapDocument Clone() => JsonUtility.FromJson<MapDocument>(JsonUtility.ToJson(this));

        public bool IsValid(out string error)
        {
            error = null;
            if (formatVersion != 1) error = "지원하지 않는 맵 파일 버전이에요.";
            else if (string.IsNullOrWhiteSpace(name) || name.Length > 120) error = "맵 이름은 1~120자로 입력해 주세요.";
            else if (!Finite(spawn) || !Finite(goal) || !Finite(killY)) error = "맵 좌표가 올바르지 않아요.";
            else if (jump == null || !Range(jump.sensitivity, .1f, 3) || !Range(jump.maxSpeed, 5, 40)
                || !Range(jump.gravity, 5, 60) || !Range(jump.maxAngle, 0, 85) || !Range(jump.minStrength, 0, 1))
                error = "점프 설정이 허용 범위를 벗어났어요.";
            else if (platforms == null || platforms.Count == 0 || platforms.Count > 2000)
                error = "발판은 1~2000개가 필요해요.";
            else
            {
                var ids = new HashSet<string>();
                foreach (var p in platforms)
                {
                    if (p == null || string.IsNullOrEmpty(p.id) || !ids.Add(p.id)
                        || !Finite(p.position) || !Finite(p.size)
                        || !Range(p.size.x, .25f, 200) || !Range(p.size.y, .25f, 200))
                    {
                        error = "발판의 ID, 위치 또는 크기가 올바르지 않아요.";
                        break;
                    }
                }
            }
            return error == null;
        }

        public static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n) && Mathf.Abs(n) <= 100000;
        public static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        static bool Range(float n, float min, float max) => Finite(n) && n >= min && n <= max;
    }

    public enum LaunchStatus { Valid, TooShort, WrongDirection, InvalidViewport }

    // One immutable solution supplies both the simulation and the developer readout.
    public readonly struct LaunchSolution
    {
        public readonly LaunchStatus Status;
        public readonly Vector2 Pull;
        public readonly float InputDistance, NormalizedDistance, RawAngle, Angle, InputStrength, Strength, Speed;
        public readonly float Sensitivity, MaxSpeed, Gravity, MaxAngle, MinStrength;
        public readonly Vector2 Velocity;
        public bool IsValid => Status == LaunchStatus.Valid;
        public bool AngleLimited => IsValid && Mathf.Abs(RawAngle - Angle) > .001f;
        public bool Saturated => IsValid && Strength >= 1f;
        public bool MinimumApplied => IsValid && Strength > InputStrength;

        internal LaunchSolution(LaunchStatus status, Vector2 pull, float inputDistance, JumpSettings settings)
        {
            Status = status;
            Pull = pull;
            InputDistance = inputDistance;
            NormalizedDistance = pull.magnitude;
            Sensitivity = settings?.sensitivity ?? 0;
            MaxSpeed = settings?.maxSpeed ?? 0;
            Gravity = settings?.gravity ?? 0;
            MaxAngle = settings?.maxAngle ?? 0;
            MinStrength = settings?.minStrength ?? 0;
            RawAngle = Mathf.Atan2(pull.x, pull.y) * Mathf.Rad2Deg;
            Angle = Mathf.Clamp(RawAngle, -MaxAngle, MaxAngle);
            InputStrength = status == LaunchStatus.Valid ? Mathf.Clamp01(Sensitivity * NormalizedDistance / 240f) : 0;
            Strength = status == LaunchStatus.Valid ? Mathf.Max(MinStrength, InputStrength) : 0;
            Speed = MaxSpeed * Strength;
            var radians = Angle * Mathf.Deg2Rad;
            Velocity = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * Speed;
        }
    }

    public static class LaunchMath
    {
        public static LaunchSolution Evaluate(Vector2 press, Vector2 release, float viewportHeight, JumpSettings settings)
        {
            if (viewportHeight <= 0 || settings == null)
                return new LaunchSolution(LaunchStatus.InvalidViewport, Vector2.zero, 0, settings);
            var input = press - release;
            var pull = input * (1080f / viewportHeight);
            var status = pull.magnitude < 12 ? LaunchStatus.TooShort
                : pull.y <= 0 ? LaunchStatus.WrongDirection : LaunchStatus.Valid;
            return new LaunchSolution(status, pull, input.magnitude, settings);
        }

        public static bool TryVelocity(Vector2 press, Vector2 release, float viewportHeight,
            JumpSettings settings, out Vector2 velocity)
        {
            var solution = Evaluate(press, release, viewportHeight, settings);
            velocity = solution.Velocity;
            return solution.IsValid;
        }
    }

    // History contains authored documents only. Simulation never writes to them.
    public sealed class DocumentHistory
    {
        readonly List<string> undo = new List<string>();
        readonly List<string> redo = new List<string>();
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public void Record(MapDocument map)
        {
            undo.Add(JsonUtility.ToJson(map));
            if (undo.Count > 100) undo.RemoveAt(0);
            redo.Clear();
        }
        public MapDocument Undo(MapDocument current) => Move(undo, redo, current);
        public MapDocument Redo(MapDocument current) => Move(redo, undo, current);
        static MapDocument Move(List<string> from, List<string> to, MapDocument current)
        {
            if (from.Count == 0) return current;
            to.Add(JsonUtility.ToJson(current));
            var json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            return JsonUtility.FromJson<MapDocument>(json);
        }
        public void Clear() { undo.Clear(); redo.Clear(); }
    }
}
