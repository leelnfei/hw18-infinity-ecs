using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using UnityEngine;

namespace Unity.InfiniteWorld
{
    [AlwaysUpdateSystem]
    public class TerrainChunkLifecycleSystem : ComponentSystem
    {
        const int VISIBILITY = 4;
        const int GRID_WIDTH = VISIBILITY * 2 + 1;
        const int GRID_SIZE = GRID_WIDTH * GRID_WIDTH;
        
        struct ChunkGroup
        {
            [ReadOnly]
            public EntityArray entities;
            [ReadOnly]
            public ComponentDataArray<Sector> sectors;
            [ReadOnly]
            public ComponentDataArray<LOD> lods;
            [ReadOnly]
            public SubtractiveComponent<Shift> shifts;
        }

        [Inject]
        ChunkGroup chunksGroup;
        [Inject]
        TerrainChunkAssetDataSystem chunkAssetData;

        EntityArchetype archetype;

        Camera camera;

        public void Init(Camera _camera)
        {
            camera = _camera;
            archetype = EntityManager.CreateArchetype(typeof(Sector), typeof(LOD), typeof(Transform), typeof(TerrainChunkGeneratorTrigger));
        }

        protected unsafe override void OnUpdate()
        {
            var sectors = chunksGroup.sectors;
            var entities = chunksGroup.entities;

            var camPos = camera.transform.position;
            float2 cameraXY = new float2(camPos.x, camPos.z);
            int2 cameraSector = new int2((int)(cameraXY.x / Sector.SECTOR_SIZE + 0.5f), (int)(cameraXY.y / Sector.SECTOR_SIZE + 0.5f));
            int2 distOffset = new int2(VISIBILITY, VISIBILITY);
            int2 baseSector = cameraSector - distOffset;

            var grid = stackalloc uint[GRID_SIZE];
            for (int i = 0; i < GRID_SIZE; ++i)
                grid[i] = 0;

            for (int i = 0; i < sectors.Length; ++i)
            {
                int2 sector = sectors[i].value;
                int2 dist = sector - baseSector;
                
                if (dist.x < GRID_WIDTH && dist.y < GRID_WIDTH && dist.x >= 0 && dist.y >= 0)
                    grid[dist.y * GRID_WIDTH + dist.x] = 1;
                else
                {
                    chunkAssetData.DisposeChunkData(sectors[i]);
                    PostUpdateCommands.DestroyEntity(entities[i]);
                }
            }

            for (int j = 0; j < GRID_WIDTH; ++j)
            {
                for (int i = 0; i < GRID_WIDTH; ++i)
                {
                    if (grid[j * GRID_WIDTH + i] == 0)
                    {
                        var entity = EntityManager.CreateEntity(archetype);
                        EntityManager.SetComponentData(entity, new Sector(baseSector, i, j));
                        EntityManager.SetComponentData(entity, new LOD(0));
                        EntityManager.SetComponentData(entity, new Transform(float4x4.identity));
                    }
                }
            }
        }
    }
}
