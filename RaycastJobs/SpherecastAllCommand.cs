using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RaycastJobs
{
    public struct SpherecastAllCommand
    {
        private NativeArray<RaycastHit> results;
        private int maxHits;

        private NativeArray<RaycastHit>[] semiResults;
        private NativeArray<SpherecastCommand>[] semiCommands;


        [BurstCompile]
        private struct CreateCommandsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<SpherecastCommand> previousCommands;
            [ReadOnly]
            public NativeArray<RaycastHit> spherecastHits;
            [WriteOnly]
            public NativeArray<SpherecastCommand> spherecastCommands;


            public void Execute(int index)
            {
                var rayHit = spherecastHits[index];
                if (rayHit.point != default(Vector3))
                {
                    var previousCommand = previousCommands[index];
                    //little hack to bypass same collider hit in specific cases
                    var point = rayHit.point;
                    var distance = previousCommand.distance - (point - previousCommand.origin).magnitude;
                    spherecastCommands[index] = new SpherecastCommand(point, previousCommand.radius, previousCommand.direction, distance, previousCommand.layerMask);
                }
                else
                {
                    spherecastCommands[index] = default(SpherecastCommand);
                }
            }
        }

        [BurstCompile]
        private struct CombineResultsJob : IJob
        {
            public int maxHits;
            public int offset;
            [ReadOnly]
            public NativeArray<RaycastHit> semiResults;
            [WriteOnly]
            public NativeArray<RaycastHit> results;
            public void Execute()
            {
                var commandsCount = results.Length / maxHits;
                for (int i = 0; i < commandsCount; i++)
                {
                    results[i * maxHits + offset] = semiResults[i];
                }
            }
        }

        [BurstCompile]
        private struct RestoreDistancesJob : IJob
        {
            public int maxHits;
            public NativeArray<SpherecastCommand> spherecastCommands;
            public NativeArray<RaycastHit> spherecastHits;

            public void Execute()
            {
                var commandsCount = spherecastHits.Length / maxHits;
                for (int i = 1; i < commandsCount; i++)
                {
                    for (int j = i * maxHits; j < (i + 1) * maxHits; j++)
                    {
                        var hit = spherecastHits[j];
                        if (hit.point == default(Vector3))
                        {
                            break;
                        }
                        var command = spherecastCommands[i];
                        var distance = (command.origin - hit.point).magnitude - command.radius;
                        hit.distance = distance;

                        spherecastHits[j] = hit;
                    }
                }
            }
        }

        /// <summary>
        /// Jobified version of <see cref="Physics.SphereCastNonAlloc"/>
        /// </summary>
        /// <param name="results">Indexing: command[i].raycast[j] = results[i * maxHits + j]</param>
        /// <param name="maxHits">Max hits count per command</param>
        public SpherecastAllCommand(NativeArray<SpherecastCommand> commands, NativeArray<RaycastHit> results, int maxHits)
        {
            if (maxHits <= 0)
            {
                throw new System.ArgumentException("maxHits should be greater than zero");
            }
            if (results.Length < commands.Length * maxHits)
            {
                throw new System.ArgumentException("Results array length does not match maxHits count");
            }
            if (maxHits <= 1)
            {
                Debug.LogWarning("Using SpherecastAllCommand with maxHits = 1 will cause unnecessary overhead comparing to SpherecastCommand, please use that instead");
            }
            this.results = results;
            this.maxHits = maxHits;

            semiResults = new NativeArray<RaycastHit>[maxHits];
            for (int i = 0; i < maxHits; i++)
            {
                semiResults[i] = new NativeArray<RaycastHit>(commands.Length, Allocator.TempJob);
            }
            semiCommands = new NativeArray<SpherecastCommand>[maxHits];
            semiCommands[0] = commands;
            for (int i = 1; i < maxHits; i++)
            {
                semiCommands[i] = new NativeArray<SpherecastCommand>(commands.Length, Allocator.TempJob);
            }
        }

        public JobHandle Schedule(JobHandle dependency)
        {
            for (int i = 0; i < maxHits; i++)
            {
                dependency = SpherecastCommand.ScheduleBatch(semiCommands[i], semiResults[i], 16, dependency);
                if (i < maxHits - 1)
                {
                    var filter = new CreateCommandsJob
                    {
                        previousCommands = semiCommands[i],
                        spherecastHits = semiResults[i],
                        spherecastCommands = semiCommands[i + 1]
                    };
                    dependency = filter.Schedule(semiCommands[i].Length, 32, dependency);
                }
                var combineResults = new CombineResultsJob
                {
                    maxHits = maxHits,
                    semiResults = semiResults[i],
                    offset = i,
                    results = results
                };
                dependency = combineResults.Schedule(dependency);
            }
            var restoreDistances = new RestoreDistancesJob
            {
                spherecastHits = results,
                maxHits = maxHits,
                spherecastCommands = semiCommands[0]
            };
            dependency = restoreDistances.Schedule(dependency);

            return dependency;
        }

        public void Dispose()
        {
            for (int i = 0; i < maxHits; i++)
            {
                semiResults[i].Dispose();
            }
            for (int i = 1; i < maxHits; i++)
            {
                semiCommands[i].Dispose();
            }
        }
    }
}