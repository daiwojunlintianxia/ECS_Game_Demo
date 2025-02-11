using GamesTan.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace GamesTan.ECS.Game {
    public unsafe static class EntityUtil {
        public static EntityRef CreateBullet(this GameEcsWorld world) {
            var services = world.Services;
            var entityMgr = world.EntityManager;
            var entity = entityMgr.AllocBullet();
            var entityPtr = entityMgr.GetBullet(entity);
            entityPtr->TransformData.Scale = new float3(4, 4, 4);
            entityPtr->AssetData.PrefabId = services.RandomValue() > 0.3 ? 10001 : 10003;
            entityPtr->AssetData.InstancePrefabIdx = RenderWorld.Instance.GetInstancePrefabIdx(entityPtr->AssetData.PrefabId);
           
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var view = obj.AddComponent<EntityViewDebugBullet>();
            view.Entity = entity;
            view.World = world;
            view.IsControlByEntity = false;
            view.transform.position = new float3(world.Services.RandomValue() * 100, 0, world.Services.RandomValue() * 100);
            view.GetComponentInChildren<Renderer>().sharedMaterial = services.BulletMaterial;
            
            obj.name = $"{services.GlobalViewId++}_UnitID_{entity.SlotId}_PrefabID{entityPtr->AssetData.PrefabId}";
            obj.transform.SetParent(services.ViewRoot);
            entityPtr->BasicData.GObjectId = obj.GetInstanceID();
            services.Id2View.Add(obj.GetInstanceID(), obj);
          
            return entityPtr->__Data;
        }
        public static void DestroyBullet(this GameEcsWorld world, EntityRef unit) {
            var entityMgr = world.EntityManager;
            var services = world.Services;
            var ptr = entityMgr.GetBullet(unit);
            world.WorldRegion.RemoveEntity(unit,ptr->PhysicData.GridCoord);
            if (services.IsCreateView) {
                if (services.Id2View.TryGetValue(ptr->GObjectId, out var go)) {
                    GameObject.Destroy(go);
                    services.Id2View.Remove(ptr->GObjectId);
                }
            }

            entityMgr.FreeEnemy(unit);
        }
        
        public static EntityRef CreateEnemy(this GameEcsWorld world) {
            var services = world.Services;
            var entityMgr = world.EntityManager;
            var entity = entityMgr.AllocEnemy();
            var entityPtr = entityMgr.GetEnemy(entity);
            entityPtr->TransformData.Scale = new float3(1, 1, 1);
            entityPtr->AssetData.PrefabId =services.IsOnlyOnePreafb?10003:( services.RandomValue() > 0.3 ? 10001 : 10003);
            entityPtr->AssetData.InstancePrefabIdx = RenderWorld.Instance.GetInstancePrefabIdx(entityPtr->AssetData.PrefabId);
            if (services.IsCreateView) {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var view = obj.AddComponent<EntityViewDebugTest>();
                view.IsControlByEntity = true;
                view.Entity = entity;
                view.World = world;
                obj.name = $"{services.GlobalViewId++}_UnitID_{entity.SlotId}_PrefabID{entityPtr->AssetData.PrefabId}";
                obj.transform.SetParent(services.ViewRoot);
                entityPtr->GObjectId = obj.GetInstanceID();
                services.Id2View.Add(obj.GetInstanceID(), obj);
            }

            return entityPtr->__Data;
        }

        public static void DestroyEnemy(this GameEcsWorld world, EntityRef unit) {
            var entityMgr = world.EntityManager;
            var services = world.Services;
            var ptr = entityMgr.GetEnemy(unit);
            world.WorldRegion.RemoveEntity(unit,ptr->PhysicData.GridCoord);
            if (services.IsCreateView) {
                if (services.Id2View.TryGetValue(ptr->GObjectId, out var go)) {
                    GameObject.Destroy(go);
                    services.Id2View.Remove(ptr->GObjectId);
                }
            }

            entityMgr.FreeEnemy(unit);
        }
    }
}