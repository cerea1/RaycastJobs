using NUnit.Framework;
using RaycastJobs;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RaycastJobs.Tests
{
    [TestFixture]
    public class RaycastAllCommandTests
    {
        BoxCollider cubeCollider;

        MeshCollider meshCubeCollider;

        MeshCollider meshConvexCollider;


        [OneTimeSetUp]
        public void SetUp()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(20f, -20f, 0f);
            cubeCollider = cube.GetComponent<BoxCollider>();
            var cubeMeshFilter = cube.GetComponent<MeshFilter>();

            var meshCube = new GameObject();
            meshCube.transform.position = new Vector3(10f, 0f, 0f);
            meshCubeCollider = meshCube.AddComponent<MeshCollider>();
            meshCubeCollider.sharedMesh = cubeMeshFilter.sharedMesh;

            meshConvexCollider = Object.Instantiate(meshCubeCollider, new Vector3(20f, 0f, 0f), Quaternion.identity);
            meshConvexCollider.transform.rotation = new Quaternion(0.5954201f, 0.3555247f, -0.5951921f, 0.4059847f);
            var rotation = meshConvexCollider.transform.rotation;
            Debug.Log($"{rotation.x}; {rotation.y}; {rotation.z}; {rotation.w}");
            meshConvexCollider.convex = true;

            Physics.SyncTransforms();
        }


        [OneTimeTearDown]
        public void TearDown()
        {
            Object.Destroy(cubeCollider.gameObject);
            Object.Destroy(meshCubeCollider.gameObject);
            Object.Destroy(meshConvexCollider.gameObject);
        }

        [Test]
        public void CommonHitsTest([Values(2, 3, 4)] int maxHits)
        {
            var rayStart = new Vector3(0f, 4f, 0f);
            var cubeCommand = new RaycastCommand(cubeCollider.transform.position + rayStart, Vector3.down);
            var meshCubeCommand = new RaycastCommand(meshCubeCollider.transform.position + rayStart, Vector3.down);
            var meshConvexCommand = new RaycastCommand(meshConvexCollider.transform.position + rayStart, Vector3.down);
            var emptyCommand = new RaycastCommand(new Vector3(30f, 0f, 0f) + rayStart, Vector3.down);

            var commandsArray = new RaycastCommand[] { cubeCommand, meshCubeCommand, emptyCommand, meshConvexCommand };
            var commands = new NativeArray<RaycastCommand>(commandsArray, Allocator.TempJob);
            var commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            var raycastAllCommand = new RaycastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            Assert.AreEqual(cubeCollider, commandHits[maxHits * 0].collider);
            Assert.AreEqual(meshCubeCollider, commandHits[maxHits * 1].collider);
            Assert.AreEqual(null, commandHits[maxHits * 2].collider);
            Assert.AreEqual(meshConvexCollider, commandHits[maxHits * 3].collider);

            var physicsHits = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var physicsHitsCount = Physics.RaycastNonAlloc(commands[i].from, commands[i].direction, physicsHits);

                SortHits(physicsHits, physicsHitsCount);

                for (int j = 0; j < physicsHitsCount; j++)
                {
                    var physicsHit = physicsHits[j];
                    var commandHit = commandHits[i * maxHits + j];
                    RaycastHitEquality.AssertEqual(physicsHit, commandHit);

                }
                if (physicsHitsCount < maxHits)
                {
                    Assert.AreEqual(null, commandHits[i * maxHits + physicsHitsCount].collider);
                }
            }

            commandHits.Dispose();
            commands.Dispose();
            raycastAllCommand.Dispose();
        }


        /// <summary>
        /// Workaround for <see cref="Physics.RaycastNonAlloc"/> return hits being not ordered by distance
        /// </summary>
        private void SortHits(RaycastHit[] physicsHits, int hitsCount)
        {
            for (int i = 0; i < hitsCount; i++)
            {
                int closestIndex = i;
                var closestDistance = physicsHits[i].distance;
                for (int j = i + 1; j < hitsCount; j++)
                {
                    if (physicsHits[j].distance < closestDistance)
                    {
                        closestIndex = j;
                        closestDistance = physicsHits[j].distance;
                    }
                }
                if (closestIndex != i)
                {
                    var cached = physicsHits[i];
                    physicsHits[i] = physicsHits[closestIndex];
                    physicsHits[closestIndex] = cached;
                }
            }
        }

        /// <summary>
        /// Check for mesh corners hit behaviour consistency
        /// </summary>
        /// <remarks>
        /// In specific cases RaycastCommand can stuck in corner of mesh collider, 
        /// this test checks if the use of <see cref="RaycastAllCommand.minStep"/> is necessary
        /// </remarks>
        [TestCase(true)]
        [TestCase(false)]
        public void NeedForMinStepTest(bool shouldFail)
        {
            var maxHits = 4;
            var rayStart = new Vector3(0f, 4f, 0f);
            var meshConvexCommand = new RaycastCommand(meshConvexCollider.transform.position + rayStart, Vector3.down, 8f);

            var commandsArray = new RaycastCommand[] { meshConvexCommand };
            var commands = new NativeArray<RaycastCommand>(commandsArray, Allocator.TempJob);
            var commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            //setting minStep to 0f
            var raycastAllCommand = new RaycastAllCommand(commands, commandHits, maxHits, shouldFail ? 0f : 0.0001f);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            Assert.AreEqual(meshConvexCollider, commandHits[0].collider);

            var collider = commandHits[1].collider;
            if (shouldFail)
            {
                Assert.IsNotNull(collider, "RaycastHit in corner point behaviour changed, minStep may be no longer required");
            }
            else
            {
                Assert.IsNull(collider);
            }

            commandHits.Dispose();
            commands.Dispose();
            raycastAllCommand.Dispose();

        }

        [Test]
        public void FromInsideHitsTest([Values(2, 3, 4)] int maxHits)
        {
            var direction = new Vector3(1f, 1f);
            var cubeCommand = new RaycastCommand(cubeCollider.transform.position, direction);
            var meshCubeCommand = new RaycastCommand(meshCubeCollider.transform.position, direction);
            var meshConvexCommand = new RaycastCommand(meshConvexCollider.transform.position, direction);

            var commandsArray = new RaycastCommand[] { cubeCommand, meshCubeCommand, meshConvexCommand };
            var commands = new NativeArray<RaycastCommand>(commandsArray, Allocator.TempJob);
            var commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            var raycastAllCommand = new RaycastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            var physicsHits = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var physicsHitsCount = Physics.RaycastNonAlloc(commands[i].from, commands[i].direction, physicsHits);
                SortHits(physicsHits, physicsHitsCount);

                for (int j = 0; j < physicsHitsCount; j++)
                {
                    var physicsHit = physicsHits[j];
                    var commandHit = commandHits[i * maxHits + j];
                    RaycastHitEquality.AssertEqual(physicsHit, commandHit);

                }
                if (physicsHitsCount < maxHits)
                {
                    Assert.AreEqual(null, commandHits[i * maxHits + physicsHitsCount].collider);
                }
            }
            commandHits.Dispose();
            commands.Dispose();
            raycastAllCommand.Dispose();
        }

        [Test]
        public void SequentialHitsTest([Values(2, 3, 4)] int maxHits)
        {
            var rayStart = new Vector3(-2f, 0f, 0f);
            var cubeCommand = new RaycastCommand(cubeCollider.transform.position + rayStart, Vector3.right);
            var meshCubeCommand = new RaycastCommand(meshCubeCollider.transform.position + rayStart, Vector3.right);
            var meshConvexCommand = new RaycastCommand(meshConvexCollider.transform.position + rayStart, Vector3.right);
            var emptyCommand = new RaycastCommand(new Vector3(30f, 0f, 0f), Vector3.right);

            var commandsArray = new RaycastCommand[] { cubeCommand, meshCubeCommand, emptyCommand, meshConvexCommand };
            var commands = new NativeArray<RaycastCommand>(commandsArray, Allocator.TempJob);
            var commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            var raycastAllCommand = new RaycastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            Assert.AreEqual(cubeCollider, commandHits[maxHits * 0].collider);
            Assert.AreEqual(meshCubeCollider, commandHits[maxHits * 1].collider);
            Assert.AreEqual(null, commandHits[maxHits * 2].collider);
            Assert.AreEqual(meshConvexCollider, commandHits[maxHits * 3].collider);

            var physicsHits = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var physicsHitsCount = Physics.RaycastNonAlloc(commands[i].from, commands[i].direction, physicsHits);
                SortHits(physicsHits, physicsHitsCount);

                for (int j = 0; j < physicsHitsCount; j++)
                {
                    var physicsHit = physicsHits[j];
                    var commandHit = commandHits[i * maxHits + j];
                    RaycastHitEquality.AssertEqual(physicsHit, commandHit);

                }
                if (physicsHitsCount < maxHits)
                {
                    Assert.AreEqual(null, commandHits[i * maxHits + physicsHitsCount].collider);
                }
            }
            commandHits.Dispose();
            commands.Dispose();
            raycastAllCommand.Dispose();
        }
    }
}