﻿using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.InfiniteWorld
{
    public static class noisex
    {
        public static float fbm(
            float2 sector,
            float2 xy,
            int octaves, 
            float multiplier, 
            float sectorScale, 
            float persistence
        )
        {
            float value = 0.0f;
            for (int j = 0; j < octaves; ++j)
            {
                value += noise.snoise((xy + sector) * sectorScale) * multiplier;

                sectorScale *= 2.0f;
                multiplier *= persistence;
            }

            return value;
        }

        public static float turb(
            float2 sector,
            float2 xy,
            int octaves,
            float multiplier,
            float sectorScale,
            float persistence
        )
        {
            float value = 0.0f;
            for (int j = 0; j < octaves; ++j)
            {
                value += math.abs(noise.snoise((xy + sector) * sectorScale)) * multiplier;

                sectorScale *= 2.0f;
                multiplier *= persistence;
            }

            return value;
        }

        public static float ridge(
            float2 sector,
            float2 xy,
            int octaves,
            float multiplier,
            float sectorScale,
            float persistence,
            float offset
        )
        {
            float value = 0.0f;
            for (int j = 0; j < octaves; ++j)
            {
                var n = math.abs(noise.snoise((xy + sector) * sectorScale)) * multiplier;
                n = offset - n;
                n *= n;

                value += n;

                sectorScale *= 2.0f;
                multiplier *= persistence;
            }

            return value;
        }
    }

    [AlwaysUpdateSystem]
    public unsafe class TerrainGenerationSystem : JobComponentSystem
    {
        public static float DoNoise(int2 sector, float x, float y)
        {
            const float invScale = 1.0f / (float)(WorldChunkConstants.ChunkSize - 1);
            return noisex.turb(
                sector,
                new float2(x, y) * invScale,
                WorldChunkConstants.TerrainOctaves,
                WorldChunkConstants.TerrainOctaveMultiplier, 1.0f, WorldChunkConstants.TerrainOctavePersistence
            );
        }

        struct GenerateHeightmapJob : IJobParallelFor
        {
            [ReadOnly] public Sector Sector;
            [WriteOnly] public NativeArray<float> Heightmap;

            public void Execute(int i)
            {
                var y = i / WorldChunkConstants.ChunkSize;
                var x = i % WorldChunkConstants.ChunkSize;

                Heightmap[i] = DoNoise(Sector.value, x, y);
            }
        }

        struct GenerateNormalmapJob : IJobParallelFor
        {
            [ReadOnly] public Sector Sector;
            [ReadOnly] public NativeArray<float> Heightmap;
            [WriteOnly] public NativeArray<float2> Normalmap;

            public void Execute(int i)
            {
                const float invScale = 1.0f / (float)(WorldChunkConstants.ChunkSize - 1);

                // Calcul normal map
                var y = i / WorldChunkConstants.ChunkSize;
                var x = i % WorldChunkConstants.ChunkSize;

                float left, right, top, bottom;
                int l = x - 1;
                int r = x + 1;
                int t = y + 1;
                int b = y - 1;

                if (l >= 0)
                    left = Heightmap[l + y * WorldChunkConstants.ChunkSize];
                else
                    left = DoNoise(Sector.value - new int2(1, 0), WorldChunkConstants.ChunkSize - 1.0f, y);
                
                if (r < WorldChunkConstants.ChunkSize)
                    right = Heightmap[r + y * WorldChunkConstants.ChunkSize];
                else
                    right = DoNoise(Sector.value + new int2(1, 0), 0.0f, y);
                
                if (t < WorldChunkConstants.ChunkSize)
                    top = Heightmap[x + t * WorldChunkConstants.ChunkSize];
                else
                    top = DoNoise(Sector.value + new int2(0, 1), x, 0.0f);
                
                if (b >= 0)
                    bottom = Heightmap[x + b * WorldChunkConstants.ChunkSize];
                else
                    bottom = DoNoise(Sector.value - new int2(0, 1), x, WorldChunkConstants.ChunkSize - 1.0f);

                var dx = ((right - left) + 1) * 0.5f;
                var dy = ((top - bottom) + 1) * 0.5f;

                var luma = new float2(dx, dy);
                Normalmap[i] = luma;
            }
        }

        struct GenerateSplatmapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Heightmap;
            // Splatmap data is stored internally as a 3d array of floats [terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers]
            [WriteOnly] public NativeArray<float4> Splatmap;

            public void Execute(int i)
            {

                // Calcul splat map
                var x = i / WorldChunkConstants.ChunkSize;
                var y = i % WorldChunkConstants.ChunkSize;

                // Normalise x/y coordinates to range 0-1 
                var xN = math.clamp(x, 0, WorldChunkConstants.ChunkSize - 1);
                var yN = math.clamp(y, 0, WorldChunkConstants.ChunkSize - 1);

                //var dx = ((Heightmap[xLeft] - Heightmap[xRight]) + 1) * 0.5f;
                //var dy = ((Heightmap[yUp] - Heightmap[yDown]) + 1) * 0.5f;

                //THE RULES BELOW TO SET THE WEIGHTS OF EACH TEXTURE
                // Texture[0] has constant influence
                var w = new float4(0.5f, 0.5f, 0.5f, 0.5f);

                //// Texture[1] is stronger at lower altitudes
                //var w1 = 0.5f;// Mathf.Clamp01((terrainData.heightmapHeight - height));

                //// Texture[2] stronger on flatter terrain
                //var w2 = 0.5f;// 1.0f - Mathf.Clamp01(steepness * steepness / (terrainData.heightmapHeight / 5.0f));

                //// Texture[3] increases with height but only on surfaces facing positive Z axis 
                //var w3 = 0.5f;// height * Mathf.Clamp01(normal.z);

                // Sum of all textures weights must add to 1, so calculate normalization factor from sum of weights
                w = math.normalize(w);// [w1, w2, w3, w4]
                Splatmap[i] = w;

            }
        }

        struct TriggeredSectors
        {
            [ReadOnly] public EntityArray Entities;
            [ReadOnly] public ComponentDataArray<TerrainChunkGeneratorTrigger> Triggers;
            [ReadOnly] public ComponentDataArray<Sector> Sectors;
            //HeightMap
            [ReadOnly] public SubtractiveComponent<TerrainChunkHasHeightmap> NotHasHeightmap;
            [ReadOnly] public SubtractiveComponent<TerrainChunkIsHeightmapBakingComponent> NotIsBakingHeightmap;
            //NormalMap
            public SubtractiveComponent<TerrainChunkHasNormalmap> NotHasNormalmap;
            public SubtractiveComponent<TerrainChunkIsNormalmapBakingComponent> NotIsBakingNormalmap;
            //SplatMap
            public SubtractiveComponent<TerrainChunkHasSplatmap> NotHasSplatmap;
            public SubtractiveComponent<TerrainChunkIsSplatmapBakingComponent> NotIsBakingSplatmap;
        }

        struct DataToUploadOnGPU
        {
            public JobHandle Handle;
            public Sector Sector;
            public Entity Entity;
        }

        class EntityBarrier : BarrierSystem
        { }

        [Inject] TriggeredSectors m_TriggeredSectors;
        [Inject] TerrainChunkAssetDataSystem m_TerrainChunkAssetDataSystem;
        [Inject] EntityBarrier m_EntityBarrier;

        List<DataToUploadOnGPU> m_DataToUploadOnGPU = new List<DataToUploadOnGPU>();

        protected override JobHandle OnUpdate(JobHandle dependsOn)
        {
            var cmd = m_EntityBarrier.CreateCommandBuffer();
            // Upload to GPU datas that are ready
            for (int i = m_DataToUploadOnGPU.Count - 1; i >= 0; --i)
            {
                var data = m_DataToUploadOnGPU[i];
                if (data.Handle.IsCompleted)
                {
                    data.Handle.Complete();
                    //HeightMap
                    var heightmap = m_TerrainChunkAssetDataSystem.GetChunkHeightmap(data.Sector);
                    var heightmapTex = m_TerrainChunkAssetDataSystem.GetChunkHeightmapTex(data.Sector);
                    heightmapTex.LoadRawTextureData(heightmap);
                    heightmapTex.Apply();
                    cmd.RemoveComponent<TerrainChunkIsHeightmapBakingComponent>(data.Entity);
                    cmd.AddComponent(data.Entity, new TerrainChunkHasHeightmap());
                    //NormalMap
                    var normalmap = m_TerrainChunkAssetDataSystem.GetChunkNormalmap(data.Sector);
                    var normalmapTex = m_TerrainChunkAssetDataSystem.GetChunkNormalmapTex(data.Sector);
                    normalmapTex.LoadRawTextureData(normalmap);
                    normalmapTex.Apply();
                    cmd.RemoveComponent<TerrainChunkIsNormalmapBakingComponent>(data.Entity);
                    cmd.AddComponent(data.Entity, new TerrainChunkHasNormalmap());
                    //SplatMap
                    var splatmap = m_TerrainChunkAssetDataSystem.GetChunkSplatmap(data.Sector);
                    var splatmapTex = m_TerrainChunkAssetDataSystem.GetChunkSplatmapTex(data.Sector);
                    splatmapTex.LoadRawTextureData(splatmap);
                    splatmapTex.Apply();
                    cmd.RemoveComponent<TerrainChunkIsSplatmapBakingComponent>(data.Entity);
                    cmd.AddComponent(data.Entity, new TerrainChunkHasSplatmap());

                    m_DataToUploadOnGPU.RemoveAt(i);
                }
            }

            // Update sectors
            if (m_TriggeredSectors.Sectors.Length > 0)
            {
                var jobHandles = new NativeArray<JobHandle>(m_TriggeredSectors.Sectors.Length, Allocator.TempJob);
                for (int i = 0, c = m_TriggeredSectors.Sectors.Length; i < c; ++i)
                {
                    var entity = m_TriggeredSectors.Entities[i];
                    var sector = m_TriggeredSectors.Sectors[i];
                    var heightmap = m_TerrainChunkAssetDataSystem.GetChunkHeightmap(sector);
                    var normalmap = m_TerrainChunkAssetDataSystem.GetChunkNormalmap(sector);
                    var splatmap = m_TerrainChunkAssetDataSystem.GetChunkSplatmap(sector);
                    JobHandle thisChunkJob = dependsOn;

                    {
                        //HeightMap
                        var job = new GenerateHeightmapJob
                        {
                            Sector = sector,
                            Heightmap = heightmap
                        };

                        thisChunkJob = job.Schedule(
                            WorldChunkConstants.ChunkSize * WorldChunkConstants.ChunkSize,
                            64,
                            dependsOn
                        );
                        //NormalMap
                        var job2 = new GenerateNormalmapJob
                        {
                            Heightmap = heightmap,
                            Normalmap = normalmap,
                            Sector = sector
                        };

                        thisChunkJob = job2.Schedule(
                            WorldChunkConstants.ChunkSize * WorldChunkConstants.ChunkSize,
                            64,
                            thisChunkJob
                        );
                        //SplatMap
                        var job3 = new GenerateSplatmapJob
                        {
                            Heightmap = heightmap,
                            Splatmap = splatmap
                        };

                        thisChunkJob = job3.Schedule(
                            WorldChunkConstants.ChunkSize * WorldChunkConstants.ChunkSize,
                            64,
                            thisChunkJob
                        );
                    }

                    cmd.AddComponent(entity, new TerrainChunkIsHeightmapBakingComponent());
                    cmd.AddComponent(entity, new TerrainChunkIsNormalmapBakingComponent());
                    cmd.AddComponent(entity, new TerrainChunkIsSplatmapBakingComponent());

                    m_DataToUploadOnGPU.Add(new DataToUploadOnGPU
                    {
                        Handle = thisChunkJob,
                        Sector = sector,
                        Entity = entity
                    });

                    jobHandles[i] = thisChunkJob;
                }
                dependsOn = JobHandle.CombineDependencies(jobHandles);
                jobHandles.Dispose();
            }

            return dependsOn;
        }
    }
}
