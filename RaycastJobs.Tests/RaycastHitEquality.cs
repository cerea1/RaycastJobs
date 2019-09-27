using NUnit.Framework;
using UnityEngine;

namespace RaycastJobs.Tests
{
    public static class RaycastHitEquality
    {
        public static void AssertEqual(RaycastHit expected, RaycastHit actual)
        {
            Assert.AreEqual(expected.collider, actual.collider);
            AssertEqual(expected.distance, actual.distance);
            AssertEqual(expected.point, actual.point);
            AssertEqual(expected.normal, actual.normal);
        }

        public static void AssertEqual(Vector3 expected, Vector3 actual)
        {
            var delta = actual - expected;

            Assert.That(delta.sqrMagnitude, Is.LessThan(0.0001f));
        }
        public static void AssertEqual(float expected, float actual)
        {
            var delta = actual - expected;

            Assert.That(delta, Is.LessThan(0.0001f));
        }
    }
}