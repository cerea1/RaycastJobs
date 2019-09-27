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
    public class SpherecastAllCommandTests
    {
        BoxCollider cubeCollider;

        MeshCollider meshCubeCollider;

        MeshCollider meshConvexCollider;

        NativeArray<RaycastHit> commandHits;
        NativeArray<SpherecastCommand> commands;
        SpherecastAllCommand raycastAllCommand;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeCollider = cube.GetComponent<BoxCollider>();
            var cubeMeshFilter = cube.GetComponent<MeshFilter>();

            var meshCube = new GameObject();
            meshCube.transform.position = new Vector3(10f, 0f, 0f);
            meshCubeCollider = meshCube.AddComponent<MeshCollider>();
            meshCubeCollider.sharedMesh = cubeMeshFilter.sharedMesh;

            meshConvexCollider = Object.Instantiate(meshCubeCollider, new Vector3(20f, 0f, 0f), Quaternion.identity);
            meshConvexCollider.transform.rotation = new Quaternion(0.5954201f, 0.3555247f, -0.5951921f, 0.4059847f);
            meshConvexCollider.convex = true;

            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Object.Destroy(cubeCollider.gameObject);
            Object.Destroy(meshCubeCollider.gameObject);
            Object.Destroy(meshConvexCollider.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            commandHits.Dispose();
            commands.Dispose();
            raycastAllCommand.Dispose();
        }

        [Test]
        public void SingleHitsTest()
        {
            var maxHits = 4;
            var rayStart = new Vector3(0f, 2f, 0f);
            var cubeCommand = new SpherecastCommand(cubeCollider.transform.position + rayStart, 1f, Vector3.down);
            var meshCubeCommand = new SpherecastCommand(meshCubeCollider.transform.position + rayStart, 1f, Vector3.down);
            var meshConvexCommand = new SpherecastCommand(meshConvexCollider.transform.position + rayStart, 1f, Vector3.down);
            var emptyCommand = new SpherecastCommand(new Vector3(30f, 0f, 0f) + rayStart, 1f, Vector3.down);

            var commandsArray = new SpherecastCommand[] { cubeCommand, meshCubeCommand, emptyCommand, meshConvexCommand };
            commands = new NativeArray<SpherecastCommand>(commandsArray, Allocator.TempJob);
            commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            raycastAllCommand = new SpherecastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            Assert.AreEqual(cubeCollider, commandHits[maxHits * 0].collider);
            Assert.AreEqual(meshCubeCollider, commandHits[maxHits * 1].collider);
            Assert.AreEqual(null, commandHits[maxHits * 2].collider);

            var physicsHits = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var unityHitsCount = Physics.SphereCastNonAlloc(commands[i].origin, commands[i].radius, commands[i].direction, physicsHits);

                for (int j = 0; j < unityHitsCount; j++)
                {
                    var physicsHit = physicsHits[j];
                    var commandHit = commandHits[i * maxHits + j];
                    RaycastHitEquality.AssertEqual(physicsHit, commandHit);

                }
                if (unityHitsCount < maxHits)
                {
                    Assert.AreEqual(null, commandHits[i * maxHits + unityHitsCount].collider);
                }
            }
        }


        [Test]
        public void FromInsideHitsTest()
        {
            var maxHits = 4;
            var direction = new Vector3(1f, 1f).normalized;
            var cubeCommand = new SpherecastCommand(cubeCollider.transform.position, 0.1f, direction);
            var meshCubeCommand = new SpherecastCommand(meshCubeCollider.transform.position, 0.1f, direction);
            var meshConvexCommand = new SpherecastCommand(meshConvexCollider.transform.position, 0.1f, direction);

            var commandsArray = new SpherecastCommand[] { cubeCommand, meshCubeCommand, meshConvexCommand };
            commands = new NativeArray<SpherecastCommand>(commandsArray, Allocator.TempJob);
            commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            raycastAllCommand = new SpherecastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            for (int i = 0; i < commands.Length; i++)
            {
                Assert.IsNull(commandHits[i * maxHits].collider, "SpherecastCommand behaviour changed, spherecasting from inside now returns collision");
            }
        }

        [Test]
        public void NeedForMinStepTest()
        {
            var maxHits = 4;
            var rayStart = new Vector3(0f, 4f, 0f);
            var meshConvexCommand = new SpherecastCommand(meshConvexCollider.transform.position + rayStart, 0.1f, Vector3.down);

            var commandsArray = new SpherecastCommand[] { meshConvexCommand };
            commands = new NativeArray<SpherecastCommand>(commandsArray, Allocator.TempJob);
            commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            //setting minStep to 0f
            raycastAllCommand = new SpherecastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            Assert.AreEqual(meshConvexCollider, commandHits[0].collider);

            var physicsHits = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var unityHitsCount = Physics.SphereCastNonAlloc(commands[i].origin, commands[i].radius, commands[i].direction, physicsHits);

                for (int j = 0; j < unityHitsCount; j++)
                {
                    var physicsHit = physicsHits[j];
                    var commandHit = commandHits[i * maxHits + j];
                    RaycastHitEquality.AssertEqual(physicsHit, commandHit);

                }
                if (unityHitsCount < maxHits)
                {
                    Assert.IsNull(commandHits[i * maxHits + unityHitsCount].collider, "RaycastHit in corner point behaviour changed, minStep may be required");
                }
            }

        }

        [Test]
        public void SequentialHitsTest()
        {
            var maxHits = 4;
            var rayStart = new Vector3(-2f, 0f, 0f);
            var cubeCommand = new SpherecastCommand(cubeCollider.transform.position + rayStart, 1f, Vector3.right);
            var meshCubeCommand = new SpherecastCommand(meshCubeCollider.transform.position + rayStart, 1f, Vector3.right);
            var meshConvexCommand = new SpherecastCommand(meshConvexCollider.transform.position + rayStart, 1f, Vector3.right);
            var emptyCommand = new SpherecastCommand(new Vector3(30f, 0f, 0f), 1f, Vector3.right);

            var commandsArray = new SpherecastCommand[] { cubeCommand, meshCubeCommand, emptyCommand, meshConvexCommand };
            commands = new NativeArray<SpherecastCommand>(commandsArray, Allocator.TempJob);
            commandHits = new NativeArray<RaycastHit>(commands.Length * maxHits, Allocator.TempJob);

            raycastAllCommand = new SpherecastAllCommand(commands, commandHits, maxHits);

            raycastAllCommand.Schedule(default(JobHandle)).Complete();

            var physicsHits = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var unityHitsCount = Physics.SphereCastNonAlloc(commands[i].origin, commands[i].radius, commands[i].direction, physicsHits);

                for (int j = 0; j < unityHitsCount; j++)
                {
                    var physicsHit = physicsHits[j];
                    var commandHit = commandHits[i * maxHits + j];
                    RaycastHitEquality.AssertEqual(physicsHit, commandHit);

                }
                if (unityHitsCount < maxHits)
                {
                    Assert.AreEqual(null, commandHits[i * maxHits + unityHitsCount].collider);
                }
            }
        }
    }
}