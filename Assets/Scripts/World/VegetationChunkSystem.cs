﻿using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.InfiniteWorld
{
    public class VegetationChunkSystem : ComponentSystem
    {
        // chunks that are visible, have heightmap and doesn't have VegetationComponent
        struct ChunkGroup
        {
            [ReadOnly]
            public ComponentDataArray<Sector> sectors;
            [ReadOnly]
            public ComponentDataArray<TerrainChunkHasHeightmap> lods;
            [ReadOnly]
            public SubtractiveComponent<TerrainChunkHasVegetation> vegetationTag;
        }

        [Inject]
        ChunkGroup chunkGroup;

        [Inject]
        TerrainChunkAssetDataSystem dataSystem;

        RandomProvider randomGen = new RandomProvider(12345);
        EntityArchetype vegetationArchetype;

        protected override void OnCreateManager(int capacity)
        {
            vegetationArchetype = EntityManager.CreateArchetype(typeof(Sector), typeof(Shift), typeof(Transform), typeof(MeshRender));

            //prefab = Resources.Load<GameObject>("Art/Tree");
            //Assert.AreNotEqual(null, prefab);
        }

        protected override void OnUpdate()
        {
            for(int temp = 0; temp < chunkGroup.sectors.Length; ++temp)
            {
                // Just create 10 trees in random position inside a sector
                for (int i = 0; i < 10; ++i)
                    CreateEntity(chunkGroup.sectors[temp]);
            }
        }

        protected void CreateEntity(Sector sector)
        {
            var heightMap = dataSystem.GetChunkHeightmap(sector);

            var rand = new uint2(randomGen.Next() % WorldChunkConstants.ChunkSize, randomGen.Next() % WorldChunkConstants.ChunkSize);
            Vector3 shift = new Vector3(rand.x, heightMap[(int)(rand.y * WorldChunkConstants.ChunkSize + rand.x)], rand.y);

            PostUpdateCommands.CreateEntity(vegetationArchetype);
            PostUpdateCommands.SetComponent(new Sector(sector.value));
            PostUpdateCommands.SetComponent(new Shift(shift));
            PostUpdateCommands.SetComponent(new MeshRender());
        }
    }
}