using NUnit.Framework;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class CaveSurfaceClumpFieldTests
    {
        private const float HorizontalCellSize = 2.5f;
        private const float VerticalCellSize = 3f;
        private const int WorldSeed = 4242;
        private const int SeedSalt = 1009;

        private static readonly Vector2 HeightRange = new Vector2(0.72f, 1.35f);
        private static readonly Vector2 WidthRange = new Vector2(0.85f, 1.2f);

        [Test]
        public void PositionsInOneCell_ShareClumpAttributes()
        {
            CaveSurfaceClumpAttributes first = Sample(
                new Vector3(10.1f, 20.1f, 30.1f));
            CaveSurfaceClumpAttributes second = Sample(
                new Vector3(10.9f, 20.9f, 30.9f));

            Assert.That(
                second.HeightMultiplier,
                Is.EqualTo(first.HeightMultiplier));
            Assert.That(second.WidthMultiplier, Is.EqualTo(first.WidthMultiplier));
            Assert.That(second.YawBiasDegrees, Is.EqualTo(first.YawBiasDegrees));
        }

        [Test]
        public void SeparateCells_ProduceDifferentAttributes()
        {
            CaveSurfaceClumpAttributes first = Sample(Vector3.zero);
            var distinct = false;
            for (int cell = 1; cell < 12 && !distinct; cell++)
            {
                CaveSurfaceClumpAttributes other = Sample(
                    new Vector3(cell * HorizontalCellSize, 0f, 0f));
                distinct = !Mathf.Approximately(
                    other.HeightMultiplier,
                    first.HeightMultiplier);
            }
            Assert.That(
                distinct,
                Is.True,
                "Neighbouring clumps must differ, or the field is a flat carpet.");
        }

        [Test]
        public void SectionSeams_ResolveToTheSameClump()
        {
            // The same world position reached from either side of a chunk boundary
            // must land in one clump, otherwise a visible seam appears where
            // sections meet. This is why the field is keyed on world coordinates
            // rather than section-local ones.
            const int sectionWidth = 32;
            var fromLowSection = new Vector3(
                0f * sectionWidth + 31.9f,
                40f,
                12f);
            var fromHighSection = new Vector3(
                1f * sectionWidth + -0.1f,
                40f,
                12f);

            Assert.That(
                fromHighSection.x,
                Is.EqualTo(fromLowSection.x).Within(1e-4f),
                "Fixture sanity: both expressions must name one world position.");

            Vector3Int lowCell = CaveSurfaceClumpField.GetCell(
                fromLowSection,
                HorizontalCellSize,
                VerticalCellSize);
            Vector3Int highCell = CaveSurfaceClumpField.GetCell(
                fromHighSection,
                HorizontalCellSize,
                VerticalCellSize);
            Assert.That(highCell, Is.EqualTo(lowCell));

            CaveSurfaceClumpAttributes low = Sample(fromLowSection);
            CaveSurfaceClumpAttributes high = Sample(fromHighSection);
            Assert.That(high.HeightMultiplier, Is.EqualTo(low.HeightMultiplier));
            Assert.That(high.YawBiasDegrees, Is.EqualTo(low.YawBiasDegrees));
        }

        [Test]
        public void StackedLedges_UseSeparateVerticalCells()
        {
            Vector3Int lower = CaveSurfaceClumpField.GetCell(
                new Vector3(4f, 10f, 4f),
                HorizontalCellSize,
                VerticalCellSize);
            Vector3Int upper = CaveSurfaceClumpField.GetCell(
                new Vector3(4f, 10f + VerticalCellSize * 2f, 4f),
                HorizontalCellSize,
                VerticalCellSize);
            Assert.That(upper.y, Is.Not.EqualTo(lower.y));
        }

        [Test]
        public void Attributes_StayInsideTheAuthoredRanges()
        {
            for (int i = 0; i < 256; i++)
            {
                CaveSurfaceClumpAttributes attributes = Sample(
                    new Vector3(i * 3.7f, i * 1.3f, i * -2.9f));
                Assert.That(
                    attributes.HeightMultiplier,
                    Is.InRange(HeightRange.x, HeightRange.y));
                Assert.That(
                    attributes.WidthMultiplier,
                    Is.InRange(WidthRange.x, WidthRange.y));
                Assert.That(attributes.YawBiasDegrees, Is.InRange(-35f, 35f));
            }
        }

        [Test]
        public void Attributes_RemainVariedFarFromTheOrigin()
        {
            // An integer hash must keep working at cave-scale coordinates; a
            // frac(sin(...)) hash bands badly once positions reach the thousands.
            var values = new System.Collections.Generic.HashSet<float>();
            for (int cell = 0; cell < 64; cell++)
            {
                CaveSurfaceClumpAttributes attributes = Sample(
                    new Vector3(
                        2000f + cell * HorizontalCellSize,
                        180f,
                        -3000f));
                values.Add(attributes.HeightMultiplier);
            }
            Assert.That(values.Count, Is.GreaterThan(32));
        }

        [Test]
        public void DegenerateCellSize_DoesNotDivideByZero()
        {
            CaveSurfaceClumpAttributes attributes = CaveSurfaceClumpField.Sample(
                new Vector3(1f, 2f, 3f),
                0f,
                0f,
                HeightRange,
                WidthRange,
                35f,
                WorldSeed,
                SeedSalt);
            Assert.That(
                attributes.HeightMultiplier,
                Is.InRange(HeightRange.x, HeightRange.y));
        }

        private static CaveSurfaceClumpAttributes Sample(Vector3 worldVoxelPosition)
        {
            return CaveSurfaceClumpField.Sample(
                worldVoxelPosition,
                HorizontalCellSize,
                VerticalCellSize,
                HeightRange,
                WidthRange,
                35f,
                WorldSeed,
                SeedSalt);
        }
    }
}
