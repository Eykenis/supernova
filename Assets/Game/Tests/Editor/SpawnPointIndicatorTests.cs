using NUnit.Framework;
using Supernova.UI;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class SpawnPointIndicatorTests
    {
        private static readonly Rect ScreenBounds =
            new Rect(0f, 0f, 1920f, 1080f);

        [Test]
        public void ShouldShow_HidesAtAndInsideFiveMetres()
        {
            Vector3 spawnPosition = new Vector3(10f, 2f, -3f);

            Assert.That(
                SpawnPointIndicator.Layout.ShouldShow(
                    spawnPosition + Vector3.forward * 4.99f,
                    spawnPosition,
                    5f),
                Is.False);
            Assert.That(
                SpawnPointIndicator.Layout.ShouldShow(
                    spawnPosition + Vector3.forward * 5f,
                    spawnPosition,
                    5f),
                Is.False);
            Assert.That(
                SpawnPointIndicator.Layout.ShouldShow(
                    spawnPosition + Vector3.forward * 5.01f,
                    spawnPosition,
                    5f),
                Is.True);
        }

        [Test]
        public void Calculate_KeepsVisibleSpawnAtProjectedScreenPosition()
        {
            Vector3 projectedPoint = new Vector3(420f, 680f, 12f);

            SpawnPointIndicator.Placement placement =
                SpawnPointIndicator.Layout.Calculate(
                    projectedPoint,
                    ScreenBounds,
                    56f);

            Assert.That(placement.IsClamped, Is.False);
            Assert.That(
                placement.ScreenPosition,
                Is.EqualTo((Vector2)projectedPoint));
        }

        [Test]
        public void Calculate_ClampsOffscreenSpawnToCorrespondingEdge()
        {
            Vector3 projectedPoint = new Vector3(2600f, 810f, 12f);

            SpawnPointIndicator.Placement placement =
                SpawnPointIndicator.Layout.Calculate(
                    projectedPoint,
                    ScreenBounds,
                    56f);

            Assert.That(placement.IsClamped, Is.True);
            Assert.That(
                placement.ScreenPosition.x,
                Is.EqualTo(ScreenBounds.xMax - 56f).Within(0.001f));
            Assert.That(
                placement.ScreenPosition.y,
                Is.GreaterThan(ScreenBounds.center.y));
        }

        [Test]
        public void Calculate_BehindCameraUsesTheOppositeProjectedDirection()
        {
            Vector3 projectedBehind =
                new Vector3(300f, ScreenBounds.center.y, -4f);

            SpawnPointIndicator.Placement placement =
                SpawnPointIndicator.Layout.Calculate(
                    projectedBehind,
                    ScreenBounds,
                    56f);

            Assert.That(placement.IsClamped, Is.True);
            Assert.That(placement.Direction.x, Is.GreaterThan(0f));
            Assert.That(
                placement.ScreenPosition.x,
                Is.EqualTo(ScreenBounds.xMax - 56f).Within(0.001f));
        }

        [Test]
        public void CalculateDistanceAlpha_FadesFromThirtyFiveToOneHundredFiftyMetres()
        {
            Assert.That(
                SpawnPointIndicator.Layout.CalculateDistanceAlpha(
                    35f,
                    35f,
                    150f),
                Is.EqualTo(1f));
            Assert.That(
                SpawnPointIndicator.Layout.CalculateDistanceAlpha(
                    92.5f,
                    35f,
                    150f),
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(
                SpawnPointIndicator.Layout.CalculateDistanceAlpha(
                    150f,
                    35f,
                    150f),
                Is.EqualTo(0f));
            Assert.That(
                SpawnPointIndicator.Layout.CalculateDistanceAlpha(
                    200f,
                    35f,
                    150f),
                Is.EqualTo(0f));
        }
    }
}
