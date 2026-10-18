using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Moonbird.Tests
{
    public sealed class CoreTests
    {
        [Test]
        public void OneWheelStepMagnifiesByThirtyFivePercent()
        {
            Assert.That(8 / EditorNavigation.Zoom(8, 1), Is.EqualTo(1.35f).Within(.0001f));
            Assert.That(EditorNavigation.Zoom(8, -1), Is.EqualTo(8 * 1.35f).Within(.0001f));
        }

        [Test]
        public void OppositeWheelStepsRestoreZoom()
        {
            var zoomed = EditorNavigation.Zoom(8, 3);
            Assert.That(EditorNavigation.Zoom(zoomed, -3), Is.EqualTo(8).Within(.0001f));
        }

        [TestCase(1, 8, 1)]
        [TestCase(100, -8, 100)]
        public void ZoomStaysWithinUsefulEditorLimits(float original, float steps, float expected)
        {
            Assert.That(EditorNavigation.Zoom(original, steps), Is.EqualTo(expected).Within(.0001f));
        }

        [Test]
        public void FractionalWheelInputRemainsSmooth()
        {
            var partial = EditorNavigation.Zoom(8, .5f);
            Assert.That(partial, Is.LessThan(8).And.GreaterThan(EditorNavigation.Zoom(8, 1)));
            Assert.That(EditorNavigation.Zoom(partial, -.5f), Is.EqualTo(8).Within(.0001f));
        }

        [TestCase(720)]
        [TestCase(900)]
        [TestCase(1080)]
        [TestCase(1440)]
        public void EquivalentPullIsResolutionIndependent(float height)
        {
            var press = new Vector2(400, height * .6f);
            var release = press + new Vector2(-80, -200) * height / 1080;
            Assert.IsTrue(LaunchMath.TryVelocity(press, release, height, new JumpSettings(), out var actual));
            LaunchMath.TryVelocity(Vector2.zero, new Vector2(-80, -200), 1080, new JumpSettings(), out var expected);
            Assert.That(Vector2.Distance(actual, expected), Is.LessThan(.0001f));
        }

        [Test]
        public void FullPullCapsSpeed()
        {
            var settings = new JumpSettings();
            LaunchMath.TryVelocity(Vector2.zero, new Vector2(0, -240), 1080, settings, out var full);
            LaunchMath.TryVelocity(Vector2.zero, new Vector2(0, -5000), 1080, settings, out var excessive);
            Assert.That(full.y, Is.EqualTo(18).Within(.001f));
            Assert.That(excessive.magnitude, Is.EqualTo(18).Within(.001f));
        }

        [Test]
        public void SensitivityScalesBeforeSpeedCap()
        {
            var settings = new JumpSettings { sensitivity = 2 };
            LaunchMath.TryVelocity(Vector2.zero, new Vector2(0, -60), 1080, settings, out var velocity);
            Assert.That(velocity.y, Is.EqualTo(9).Within(.001f));
        }

        [TestCase(0, 24, .1f, false)]
        [TestCase(.3f, 24, .3f, true)]
        [TestCase(.3f, 72, .3f, false)]
        [TestCase(.3f, 192, .8f, false)]
        [TestCase(1, 24, 1, true)]
        [TestCase(1, 500, 1, false)]
        public void MinimumStrengthFloorsWeakPullsWithoutRescalingStrongerPulls(float minimum,
            float distance, float expected, bool applied)
        {
            var settings = new JumpSettings { minStrength = minimum, maxSpeed = 20 };
            var solution = LaunchMath.Evaluate(Vector2.zero, Vector2.down * distance, 1080, settings);
            Assert.IsTrue(solution.IsValid);
            Assert.That(solution.InputStrength, Is.EqualTo(Mathf.Clamp01(distance / 240)).Within(.0001f));
            Assert.That(solution.Strength, Is.EqualTo(expected).Within(.0001f));
            Assert.That(solution.Speed, Is.EqualTo(20 * expected).Within(.0001f));
            Assert.That(solution.MinimumApplied, Is.EqualTo(applied));
            Assert.IsTrue(LaunchMath.TryVelocity(Vector2.zero, Vector2.down * distance, 1080, settings, out var velocity));
            Assert.That(Vector2.Distance(velocity, solution.Velocity), Is.LessThan(.0001f));
        }

        [TestCase(0, 0, 1080)]
        [TestCase(0, -4, 1080)]
        [TestCase(0, 200, 1080)]
        [TestCase(100, 0, 1080)]
        [TestCase(0, -120, 0)]
        public void FullMinimumNeverTurnsCancelledInputIntoAJump(float x, float y, float height)
        {
            var solution = LaunchMath.Evaluate(Vector2.zero, new Vector2(x, y), height,
                new JumpSettings { minStrength = 1 });
            Assert.IsFalse(solution.IsValid);
            Assert.IsFalse(solution.MinimumApplied);
            Assert.AreEqual(0, solution.Strength);
            Assert.AreEqual(Vector2.zero, solution.Velocity);
        }

        [TestCase(.1f, .3f)]
        [TestCase(3, .75f)]
        public void MinimumStrengthAppliesAfterSensitivityAndPreservesAngle(float sensitivity, float expected)
        {
            var solution = LaunchMath.Evaluate(Vector2.zero, new Vector2(-36, -48), 1080,
                new JumpSettings { sensitivity = sensitivity, minStrength = .3f, maxAngle = 20 });
            Assert.That(solution.Strength, Is.EqualTo(expected).Within(.0001f));
            Assert.That(solution.Angle, Is.EqualTo(20).Within(.0001f));
        }

        [Test]
        public void LegacyMapsDefaultToZeroMinimumStrength()
        {
            var json = JsonUtility.ToJson(MapDocument.CreateDefault());
            var legacy = System.Text.RegularExpressions.Regex.Replace(json, ",\"minStrength\":0(?:\\.0+)?", "");
            Assert.That(legacy, Does.Not.Contain("minStrength"));
            var map = MapStore.Parse(legacy);
            Assert.AreEqual(0, map.jump.minStrength);
            var solution = LaunchMath.Evaluate(Vector2.zero, Vector2.down * 24, 1080, map.jump);
            Assert.That(solution.Strength, Is.EqualTo(.1f).Within(.0001f));
        }

        [TestCase(-.01f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void MapsRejectInvalidMinimumStrength(float minimum)
        {
            var map = MapDocument.CreateDefault();
            map.jump.minStrength = minimum;
            Assert.IsFalse(map.IsValid(out _));
        }

        [TestCase(-500)]
        [TestCase(500)]
        public void AngleIsClampedSymmetrically(float horizontal)
        {
            var settings = new JumpSettings { maxAngle = 35 };
            LaunchMath.TryVelocity(Vector2.zero, new Vector2(horizontal, -20), 1080, settings, out var velocity);
            Assert.That(Mathf.Abs(Mathf.Atan2(velocity.x, velocity.y) * Mathf.Rad2Deg), Is.EqualTo(35).Within(.001f));
            Assert.That(Mathf.Sign(velocity.x), Is.EqualTo(-Mathf.Sign(horizontal)));
        }

        [TestCase(0, 0)]
        [TestCase(0, -4)]
        [TestCase(0, 200)]
        [TestCase(100, 0)]
        public void InvalidPullCancels(float x, float y)
        {
            Assert.IsFalse(LaunchMath.TryVelocity(Vector2.zero, new Vector2(x, y), 1080, new JumpSettings(), out _));
        }

        [Test]
        public void ZeroAngleAlwaysLaunchesVertically()
        {
            LaunchMath.TryVelocity(Vector2.zero, new Vector2(200, -100), 1080, new JumpSettings { maxAngle = 0 }, out var velocity);
            Assert.That(velocity.x, Is.EqualTo(0).Within(.001f));
            Assert.That(velocity.y, Is.GreaterThan(0));
        }

        [Test]
        public void DefaultCourseIsValidAndCloneDoesNotShareState()
        {
            var map = MapDocument.CreateDefault();
            Assert.IsTrue(map.IsValid(out var error), error);
            var clone = map.Clone();
            clone.platforms[0].position = Vector2.one * 17;
            clone.jump.sensitivity = 2;
            Assert.AreNotEqual(map.platforms[0].position, clone.platforms[0].position);
            Assert.AreNotEqual(map.jump.sensitivity, clone.jump.sensitivity);
        }

        [TestCase(18, 1, 120, .5f, 9)]
        [TestCase(36, 1, 120, .5f, 18)]
        [TestCase(18, 2, 120, 1, 18)]
        [TestCase(18, 1, 500, 1, 18)]
        public void DeveloperStrengthIsFractionOfMaximumSpeed(float maxSpeed, float sensitivity,
            float distance, float fraction, float speed)
        {
            var solution = LaunchMath.Evaluate(Vector2.zero, Vector2.down * distance, 1080,
                new JumpSettings { maxSpeed = maxSpeed, sensitivity = sensitivity });
            Assert.IsTrue(solution.IsValid);
            Assert.That(solution.Strength, Is.EqualTo(fraction).Within(.0001f));
            Assert.That(solution.Speed, Is.EqualTo(speed).Within(.0001f));
            Assert.That(solution.NormalizedDistance, Is.EqualTo(distance).Within(.0001f));
        }

        [TestCase(-400)]
        [TestCase(400)]
        public void DeveloperShowsRawAndClampedAnglesButUsesActualVelocity(float horizontal)
        {
            var press = new Vector2(600, 500);
            var release = press + new Vector2(horizontal, -60);
            var settings = new JumpSettings { maxAngle = 40 };
            var solution = LaunchMath.Evaluate(press, release, 1080, settings);
            Assert.IsTrue(solution.AngleLimited);
            Assert.That(Mathf.Abs(solution.RawAngle), Is.GreaterThan(40));
            Assert.That(Mathf.Abs(solution.Angle), Is.EqualTo(40).Within(.0001f));
            Assert.IsTrue(LaunchMath.TryVelocity(press, release, 1080, settings, out var velocity));
            Assert.That(Vector2.Distance(solution.Velocity, velocity), Is.LessThan(.0001f));
            Assert.That(Mathf.Atan2(velocity.x, velocity.y) * Mathf.Rad2Deg, Is.EqualTo(solution.Angle).Within(.0001f));
        }

        [TestCase(720)]
        [TestCase(1440)]
        public void DeveloperDistanceHasRawPixelsAndResolutionIndependentReference(float height)
        {
            var actual = 120 * height / 1080;
            var solution = LaunchMath.Evaluate(Vector2.zero, Vector2.down * actual, height, new JumpSettings());
            Assert.That(solution.InputDistance, Is.EqualTo(actual).Within(.001f));
            Assert.That(solution.NormalizedDistance, Is.EqualTo(120).Within(.001f));
            Assert.That(solution.Strength, Is.EqualTo(.5f).Within(.0001f));
        }

        [Test]
        public void CommittedSolutionRetainsSettingsAfterTuningChanges()
        {
            var settings = new JumpSettings { minStrength = .6f };
            var solution = LaunchMath.Evaluate(Vector2.zero, Vector2.down * 120, 1080, settings);
            settings.maxSpeed = 36;
            settings.sensitivity = 2;
            settings.gravity = 60;
            settings.minStrength = 1;
            Assert.AreEqual(18, solution.MaxSpeed);
            Assert.AreEqual(1, solution.Sensitivity);
            Assert.AreEqual(30, solution.Gravity);
            Assert.That(solution.Speed, Is.EqualTo(10.8f).Within(.0001f));
            Assert.That(solution.MinStrength, Is.EqualTo(.6f).Within(.0001f));
            Assert.That(solution.InputStrength, Is.EqualTo(.5f).Within(.0001f));
        }

        [TestCase(0, -5, LaunchStatus.TooShort)]
        [TestCase(0, 100, LaunchStatus.WrongDirection)]
        public void DeveloperExplainsCancelledPullAndShowsNoLaunch(float x, float y, LaunchStatus status)
        {
            var solution = LaunchMath.Evaluate(Vector2.zero, new Vector2(x, y), 1080, new JumpSettings());
            Assert.AreEqual(status, solution.Status);
            Assert.AreEqual(Vector2.zero, solution.Velocity);
            Assert.AreEqual(0, solution.Strength);
        }

        [Test]
        public void HistoryRestoresGeometryAndSettingsAndDiscardsRedoAfterEdit()
        {
            var map = MapDocument.CreateDefault();
            var history = new DocumentHistory();
            history.Record(map);
            map.platforms[0].size = new Vector2(7, 3);
            map.jump.maxAngle = 40;
            map = history.Undo(map);
            Assert.AreEqual(new Vector2(16, 1), map.platforms[0].size);
            Assert.AreEqual(65, map.jump.maxAngle);
            map = history.Redo(map);
            Assert.AreEqual(new Vector2(7, 3), map.platforms[0].size);
            Assert.AreEqual(40, map.jump.maxAngle);
            map = history.Undo(map);
            history.Record(map);
            Assert.IsFalse(history.CanRedo);
        }

        [Test]
        public void MapRejectsDuplicateIdsAndNonpositiveSizes()
        {
            var map = MapDocument.CreateDefault();
            map.platforms[1].id = map.platforms[0].id;
            Assert.IsFalse(map.IsValid(out _));
            map = MapDocument.CreateDefault();
            map.platforms[0].size = new Vector2(-1, 0);
            Assert.IsFalse(map.IsValid(out _));
        }

        [TestCase("")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("{broken-json}")]
        public void InvalidMapIsRejected(string json)
        {
            Assert.Throws<InvalidDataException>(() => MapStore.Parse(json));
        }

        [Test]
        public void DiskRoundTripAndOverwriteWorkWithKoreanFilename()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MoonbirdTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "아기 새의 맵.json");
            var map = MapDocument.CreateDefault();
            try
            {
                MapStore.Save(path, map);
                Assert.AreEqual(JsonUtility.ToJson(map), JsonUtility.ToJson(MapStore.Load(path)));
                map.jump.maxAngle = 23;
                map.jump.minStrength = .37f;
                MapStore.Save(path, map);
                Assert.AreEqual(23, MapStore.Load(path).jump.maxAngle);
                Assert.That(MapStore.Load(path).jump.minStrength, Is.EqualTo(.37f).Within(.0001f));
                Assert.IsFalse(File.Exists(path + ".moonbird-tmp"));
            }
            finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
        }
    }
}
