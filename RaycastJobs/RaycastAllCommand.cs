using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RaycastJobs
{
    public struct RaycastAllCommand
    {
        private NativeArray<RaycastHit> results;
        private int maxHits;
        public int MaxHits { get => maxHits; }
        public readonly float minStep;

        private NativeArray<RaycastHit>[] semiResults;
        private NativeArray<RaycastCommand>[] semiCommands;


        [BurstCompile]
        private struct CreateCommandsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<RaycastCommand> previousCommands;
            [ReadOnly]
            public NativeArray<RaycastHit> raycastHits;
            [WriteOnly]
            public NativeArray<RaycastCommand> raycastCommands;

            [ReadOnly]
            public float minStep;

            public void Execute(int index)
            {
                var rayHit = raycastHits[index];
                if (rayHit.point != default(Vector3))
                {
                    var previousCommand = previousCommands[index];
                    //little hack to bypass same collider hit in specific cases
                    var point = rayHit.point + previousCommand.direction.normalized * minStep;
                    var distance = previousCommand.distance - (point - previousCommand.from).magnitude;
                    raycastCommands[index] = new RaycastCommand(point, previousCommand.direction, distance, previousCommand.layerMask, 1);
                }
                else
                {
                    raycastCommands[index] = default(RaycastCommand);
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
            public NativeArray<RaycastCommand> raycastCommands;
            public NativeArray<RaycastHit> raycastHits;

            public void Execute()
            {
                var commandsCount = raycastHits.Length / maxHits;
                for (int i = 1; i < commandsCount; i++)
                {
                    for (int j = i * maxHits; j < (i + 1) * maxHits; j++)
                    {
                        var hit = raycastHits[j];
                        if (hit.point == default(Vector3))
                        {
                            break;
                        }
                        var distance = (raycastCommands[i].from - hit.point).magnitude;
                        hit.distance = distance;

                        raycastHits[j] = hit;
                    }
                }
            }
        }


        /// <summary>
        /// Jobified version of <see cref="Physics.RaycastNonAlloc"/>
        /// </summary>
        /// <param name="commands">Array of commands to perform. Each <see cref="RaycastCommand.maxHits"/> should be 1</param>
        /// <param name="results">Indexing: command[i].raycast[j] = results[i * maxHits + j]</param>
        /// <param name="maxHits">Max hits count per command</param>
        /// <param name="minStep">Minimal distance each Raycast should progress</param>
        public RaycastAllCommand(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int maxHits, float minStep = 0.0001f)
        {
            if (maxHits <= 0)
            {
                throw new System.ArgumentException("maxHits should be greater than zero");
            }
            if (results.Length < commands.Length * maxHits)
            {
                throw new System.ArgumentException("Results array length does not match maxHits count");
            }
            if (minStep < 0f)
            {
                throw new System.ArgumentException("minStep should be more or equal to zero");
            }
            if(maxHits <= 1)
            {
                Debug.LogWarning("Using RaycastAllCommand with maxHits = 1 will cause unnecessary overhead comparing to RaycastCommand, please use that instead");
            }
            this.results = results;
            this.maxHits = maxHits;
            this.minStep = minStep;

            semiResults = new NativeArray<RaycastHit>[maxHits];
            for (int i = 0; i < maxHits; i++)
            {
                semiResults[i] = new NativeArray<RaycastHit>(commands.Length, Allocator.TempJob);
            }
            semiCommands = new NativeArray<RaycastCommand>[maxHits];
            semiCommands[0] = commands;
            for (int i = 1; i < maxHits; i++)
            {
                semiCommands[i] = new NativeArray<RaycastCommand>(commands.Length, Allocator.TempJob);
            }
        }

        public JobHandle Schedule(JobHandle dependency)
        {
            for (int i = 0; i < maxHits; i++)
            {
                dependency = RaycastCommand.ScheduleBatch(semiCommands[i], semiResults[i], 16, dependency);
                if (i < maxHits - 1)
                {
                    var filter = new CreateCommandsJob
                    {
                        previousCommands = semiCommands[i],
                        raycastHits = semiResults[i],
                        raycastCommands = semiCommands[i + 1],
                        minStep = minStep
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
                raycastHits = results,
                maxHits = maxHits,
                raycastCommands = semiCommands[0]
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
            maxHits = 0;
        }
    }
}