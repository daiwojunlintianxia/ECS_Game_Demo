﻿using Unity.Mathematics;
using UnityEngine;

namespace GamesTan.ECS.Game {
    public unsafe partial class SysTestEnemyAwake : BaseGameSystem {
        public override void Update(float dt) {
            float dist = 100;
            if (Services.DebugOnlyOneEntity) {
                dist = Services.DebugEntityBornPosRange;
            }
            var enemys = EntityManager.GetAllEnemy();
            foreach (var item in enemys) {
                var enemy = EntityManager.GetEnemy(item);
                if (!enemy->IsAlreadyStart) {
                    enemy->IsAlreadyStart = true;
                    enemy->PhysicData. Speed = 1;
                    enemy->PhysicData.RotateSpeed = 10;
                    enemy->UnitData.Health = 100;
                    enemy->Scale = (Services.RandomValue()*0.5f+0.5f);
                    enemy->TransformData.Position = new float3(Services.RandomValue() * dist, 0, Services.RandomValue() * dist);
                    
                    enemy->DegY = Services.RandomValue()*360;
                    enemy->AnimData.Timer = Services.RandomValue() * 3;
                    enemy->AnimData.AnimId1[0] = Services.RandomRange(0,3) ;
                    enemy->AnimData.AnimId1[1] = Services.RandomRange(0,3) ;  
                    WorldRegion.AddEntity(item, ref enemy->PhysicData.GridCoord,enemy->TransformData.Position);
                }
            }
        }
    }
}