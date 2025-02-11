﻿using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using GamesTan.ECS;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GamesTan.Rendering {
    public partial class IndirectRendererRuntimeData {
        public IndirectRendererConfig Config;

        public IHierarchyZBuffer hiZBuffer;

        [Header("Data")] public IndirectRenderingMesh[] indirectMeshes;
        public InstanceRenderData _rendererData = new InstanceRenderData();

        // prefab buffers
        public ComputeBuffer m_instancesArgsBuffer;

        public ComputeBuffer m_shadowArgsBuffer;

        // Compute Buffers
        public ComputeBuffer m_instancesIsVisibleBuffer;
        public ComputeBuffer m_instanceDataBuffer;
        public ComputeBuffer m_instancesSortingData;
        public ComputeBuffer m_instancesShadowSortingData;
        public ComputeBuffer m_instancesSortingDataTemp;
        public ComputeBuffer m_instancesMatrixRows01;
        public ComputeBuffer m_instancesCulledMatrixRows01;
        public ComputeBuffer m_shadowsIsVisibleBuffer;
        public ComputeBuffer m_shadowCulledMatrixRows01;

        // update buffers
        public ComputeBuffer m_transformDataBuffer;

        // animation
        public ComputeBuffer m_instancesDrawAnimData;
        public ComputeBuffer m_instancesCulledAnimData;

        public ComputeBuffer m_shadowCulledAnimData;

        // remap final index
        public ComputeBuffer m_instancesDrawIndexRemap;
        public ComputeBuffer m_instancesCulledIndexRemap;
        public ComputeBuffer m_shadowCulledIndexRemap;


        // Command Buffers

        // Kernel ID's
        public int m_createDrawDataBufferKernelID;
        public int m_sorting_128_CSKernelID;
        public int m_sorting_256_CSKernelID;
        public int m_sorting_512_CSKernelID;
        public int m_sortingTranspose_64_KernelID;
        public int m_occlusionKernelID;
        public int m_scanInstancesKernelID;
        public int m_scanGroupSumsKernelID;
        public int m_copyInstanceDataKernelID;
        public bool m_isInitialized;

        // Other
        public int m_numberOfInstanceTypes;
        public int m_numberOfInstances;
        public int m_occlusionGroupX;
        public int m_copyInstanceDataGroupX;
        public bool m_debugLastDrawLOD = false;
        public bool m_isEnabled;
        public uint[] m_args;
        public Bounds m_bounds;
        public Vector3 m_camPosition = Vector3.zero;
        public Matrix4x4 m_MVP;

        // Debug
        public StringBuilder m_debugUIText = new StringBuilder(1000);
        public Text m_uiText;
        public GameObject m_uiObj;

        public Transform transform;

        private static IndirectRendererRuntimeData _instance;
        public static IndirectRendererRuntimeData Instance {
            get => _instance;
        }
        public static void SetInstance(IndirectRendererRuntimeData instance) {
            _instance = instance;
        }

        public IndirectRendererRuntimeData(IndirectRendererConfig config) {
            Debug.Assert(config != null, " IndirectRendererConfig should not be null");
            this.Config = config;
        }

        public void DoAwake(InstanceRenderData data, List<IndirectInstanceData> prefabInfos,
            IHierarchyZBuffer hierarchyZBuffer, Transform trans = null) {
            if (m_isInitialized) {
                Debug.LogError($"{GetType().Name } Already Init ");
                return;
            }

            if (this._rendererData != null) {
                this._rendererData.OnLayoutChangedEvent -= OnRenderDataLayoutChanged;
            }

            m_isEnabled = true;
            _rendererData = data;
            this._rendererData.OnLayoutChangedEvent += OnRenderDataLayoutChanged;

            this.hiZBuffer = hierarchyZBuffer;
            this.transform = trans;
            if (!TryGetKernels()) {
                Debug.LogError("Load compute shader core failed!");
            }

            m_numberOfInstances = data.Capacity;
            InitPrefabBuffers(prefabInfos);
            InitInstanceBuffers();
            m_isInitialized = true;
        }

        public void DoDestroy() {
            if(!m_isInitialized) return;
            ReleaseBuffers();
            if (this._rendererData != null) {
                this._rendererData.OnLayoutChangedEvent -= OnRenderDataLayoutChanged;
            }

            if (debugDrawLOD) {
                for (int i = 0; i < indirectMeshes.Length; i++) {
                    indirectMeshes[i].material.DisableKeyword(DEBUG_SHADER_LOD_KEYWORD);
                }
            }

            m_isInitialized = false;
            if (hiZBuffer != null) hiZBuffer.Enabled = false;
        }

        public void DoUpdate() {
            if (m_isEnabled) {
                UpdateDebug();
            }
        }


        public void OnRenderDataLayoutChanged() {
            if (m_numberOfInstances < _rendererData.Capacity) {
                m_numberOfInstances = _rendererData.Capacity;
                InitInstanceBuffers();
            }
        }
        public void GetSortKernelInfo(uint instanceCount,out uint sortBlockSize, out int sortKernelID) {
            if (instanceCount <= 128) {
                sortBlockSize = 128;
                sortKernelID = m_sorting_128_CSKernelID;
            }
            else if (instanceCount <= 256) {
                sortBlockSize = 256;
                sortKernelID = m_sorting_256_CSKernelID;
            }
            else if (instanceCount <= 512) {
                sortBlockSize = 512;
                sortKernelID = m_sorting_512_CSKernelID;
            }
            else if (instanceCount <= 1024) {
                sortBlockSize = 128;
                sortKernelID = m_sorting_128_CSKernelID;
            }
            else if (instanceCount <= 2048) {
                sortBlockSize = 256;
                sortKernelID = m_sorting_256_CSKernelID;
            }
            else {
                sortBlockSize = 512;
                sortKernelID = m_sorting_512_CSKernelID;
            }
        }
        
        public void RenderPrepare(Camera mainCamera) {
            m_camPosition = mainCamera.transform.position;
            m_bounds.center = m_camPosition;
            m_bounds.extents = Vector3.one * 10000;

            //Matrix4x4 m = mainCamera.transform.localToWorldMatrix;
            Matrix4x4 v = mainCamera.worldToCameraMatrix;
            Matrix4x4 p = mainCamera.projectionMatrix;
            m_MVP = p * v; //*m;
        }



        private void InitInstanceBuffers() {
            Debug.LogWarning("InitInstanceBuffers " + m_numberOfInstances);
            int computeShaderInputSize = Marshal.SizeOf(typeof(InstanceBound));
            int computeShaderDrawMatrixSize = Marshal.SizeOf(typeof(IndirectMatrix));
            int computeSortingDataSize = Marshal.SizeOf(typeof(SortingData));
            int computeAnimDataSize = Marshal.SizeOf(typeof(AnimRenderData));
            int computeIndexRemapDataSize = Marshal.SizeOf(typeof(uint));
            ReleaseInstanceBuffers();

            m_instancesDrawAnimData =
                new ComputeBuffer(m_numberOfInstances, computeAnimDataSize, ComputeBufferType.Default);
            m_instancesCulledAnimData =
                new ComputeBuffer(m_numberOfInstances, computeAnimDataSize, ComputeBufferType.Default);
            m_shadowCulledAnimData =
                new ComputeBuffer(m_numberOfInstances, computeAnimDataSize, ComputeBufferType.Default);

            m_instancesDrawIndexRemap =
                new ComputeBuffer(m_numberOfInstances, computeIndexRemapDataSize, ComputeBufferType.Default);
            m_instancesCulledIndexRemap =
                new ComputeBuffer(m_numberOfInstances, computeIndexRemapDataSize, ComputeBufferType.Default);
            m_shadowCulledIndexRemap =
                new ComputeBuffer(m_numberOfInstances, computeIndexRemapDataSize, ComputeBufferType.Default);

            m_instanceDataBuffer =
                new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
            m_instancesSortingData =
                new ComputeBuffer(m_numberOfInstances, computeSortingDataSize, ComputeBufferType.Default);
            m_instancesShadowSortingData =
                new ComputeBuffer(m_numberOfInstances, computeSortingDataSize, ComputeBufferType.Default);
            m_instancesSortingDataTemp =
                new ComputeBuffer(m_numberOfInstances, computeSortingDataSize, ComputeBufferType.Default);
            m_instancesMatrixRows01 = new ComputeBuffer(m_numberOfInstances, computeShaderDrawMatrixSize,
                ComputeBufferType.Default);
            m_instancesCulledMatrixRows01 = new ComputeBuffer(m_numberOfInstances, computeShaderDrawMatrixSize,
                ComputeBufferType.Default);
            m_instancesIsVisibleBuffer =
                new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);

            m_shadowCulledMatrixRows01 = new ComputeBuffer(m_numberOfInstances, computeShaderDrawMatrixSize,
                ComputeBufferType.Default);
            m_shadowsIsVisibleBuffer = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);

            // Setup the Material Property blocks for our meshes...
            for (int i = 0; i < indirectMeshes.Length; i++) {
                IndirectRenderingMesh irm = indirectMeshes[i];
                int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE_TYPE;

                irm.lod00MatPropBlock = new MaterialPropertyBlock();
                irm.lod01MatPropBlock = new MaterialPropertyBlock();
                irm.lod02MatPropBlock = new MaterialPropertyBlock();
                irm.shadowLod00MatPropBlock = new MaterialPropertyBlock();
                irm.shadowLod01MatPropBlock = new MaterialPropertyBlock();
                irm.shadowLod02MatPropBlock = new MaterialPropertyBlock();

                irm.lod00MatPropBlock.SetInt(_ArgsOffset, argsIndex + 4);
                irm.lod01MatPropBlock.SetInt(_ArgsOffset, argsIndex + 9);
                irm.lod02MatPropBlock.SetInt(_ArgsOffset, argsIndex + 14);

                irm.shadowLod00MatPropBlock.SetInt(_ArgsOffset, argsIndex + 4);
                irm.shadowLod01MatPropBlock.SetInt(_ArgsOffset, argsIndex + 9);
                irm.shadowLod02MatPropBlock.SetInt(_ArgsOffset, argsIndex + 14);

                irm.lod00MatPropBlock.SetBuffer(_ArgsBuffer, m_instancesArgsBuffer);
                irm.lod01MatPropBlock.SetBuffer(_ArgsBuffer, m_instancesArgsBuffer);
                irm.lod02MatPropBlock.SetBuffer(_ArgsBuffer, m_instancesArgsBuffer);

                irm.shadowLod00MatPropBlock.SetBuffer(_ArgsBuffer, m_shadowArgsBuffer);
                irm.shadowLod01MatPropBlock.SetBuffer(_ArgsBuffer, m_shadowArgsBuffer);
                irm.shadowLod02MatPropBlock.SetBuffer(_ArgsBuffer, m_shadowArgsBuffer);

                irm.lod00MatPropBlock.SetBuffer(_InstancesDrawAnimData, m_instancesCulledAnimData);
                irm.lod01MatPropBlock.SetBuffer(_InstancesDrawAnimData, m_instancesCulledAnimData);
                irm.lod02MatPropBlock.SetBuffer(_InstancesDrawAnimData, m_instancesCulledAnimData);

                irm.lod00MatPropBlock.SetBuffer(_InstancesCulledIndexRemap, m_instancesCulledIndexRemap);
                irm.lod01MatPropBlock.SetBuffer(_InstancesCulledIndexRemap, m_instancesCulledIndexRemap);
                irm.lod02MatPropBlock.SetBuffer(_InstancesCulledIndexRemap, m_instancesCulledIndexRemap);


                irm.lod00MatPropBlock.SetBuffer(_InstancesDrawMatrixRows, m_instancesCulledMatrixRows01);
                irm.lod01MatPropBlock.SetBuffer(_InstancesDrawMatrixRows, m_instancesCulledMatrixRows01);
                irm.lod02MatPropBlock.SetBuffer(_InstancesDrawMatrixRows, m_instancesCulledMatrixRows01);


                irm.shadowLod00MatPropBlock.SetBuffer(_InstancesDrawAnimData, m_shadowCulledAnimData);
                irm.shadowLod01MatPropBlock.SetBuffer(_InstancesDrawAnimData, m_shadowCulledAnimData);
                irm.shadowLod02MatPropBlock.SetBuffer(_InstancesDrawAnimData, m_shadowCulledAnimData);

                irm.shadowLod00MatPropBlock.SetBuffer(_InstancesCulledIndexRemap, m_shadowCulledIndexRemap);
                irm.shadowLod01MatPropBlock.SetBuffer(_InstancesCulledIndexRemap, m_shadowCulledIndexRemap);
                irm.shadowLod02MatPropBlock.SetBuffer(_InstancesCulledIndexRemap, m_shadowCulledIndexRemap);

                irm.shadowLod00MatPropBlock.SetBuffer(_InstancesDrawMatrixRows, m_shadowCulledMatrixRows01);
                irm.shadowLod01MatPropBlock.SetBuffer(_InstancesDrawMatrixRows, m_shadowCulledMatrixRows01);
                irm.shadowLod02MatPropBlock.SetBuffer(_InstancesDrawMatrixRows, m_shadowCulledMatrixRows01);
            }

            //-----------------------------------
            // InitializeDrawData
            //-----------------------------------


            // Create the buffer containing draw data for all instances
            m_transformDataBuffer = new ComputeBuffer(m_numberOfInstances, Marshal.SizeOf(typeof(TransformData)),
                ComputeBufferType.Default);

            createDrawDataBufferCS.SetBuffer(m_createDrawDataBufferKernelID, _TransformData, m_transformDataBuffer);
            createDrawDataBufferCS.SetBuffer(m_createDrawDataBufferKernelID, _InstancesDrawMatrixRows,
                m_instancesMatrixRows01);


            //-----------------------------------
            // InitConstantComputeVariables
            //-----------------------------------

            m_occlusionGroupX = Mathf.Max(1, m_numberOfInstances / 64);
            m_copyInstanceDataGroupX = Mathf.Max(1, m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE));

            occlusionCS.SetInt(_ShouldFrustumCull, enableFrustumCulling ? 1 : 0);
            occlusionCS.SetInt(_ShouldOcclusionCull, enableOcclusionCulling ? 1 : 0);
            occlusionCS.SetInt(_ShouldDetailCull, enableDetailCulling ? 1 : 0);
            occlusionCS.SetInt(_ShouldLOD, enableLOD ? 1 : 0);
            occlusionCS.SetInt(_ShouldOnlyUseLOD02Shadows, enableOnlyLOD02Shadows ? 1 : 0);
            occlusionCS.SetFloat(_ShadowDistance, shadowDistance);
            occlusionCS.SetFloat(_DetailCullingScreenPercentage, detailCullingPercentage);
            occlusionCS.SetFloat(_Lod0Distance, lod0Distance);
            occlusionCS.SetFloat(_Lod1Distance, lod1Distance);
            occlusionCS.SetBuffer(m_occlusionKernelID, _InstanceDataBuffer, m_instanceDataBuffer);
            occlusionCS.SetBuffer(m_occlusionKernelID, _ArgsBuffer, m_instancesArgsBuffer);
            occlusionCS.SetBuffer(m_occlusionKernelID, _ShadowArgsBuffer, m_shadowArgsBuffer);
            occlusionCS.SetBuffer(m_occlusionKernelID, _IsVisibleBuffer, m_instancesIsVisibleBuffer);
            occlusionCS.SetBuffer(m_occlusionKernelID, _ShadowIsVisibleBuffer, m_shadowsIsVisibleBuffer);
            occlusionCS.SetBuffer(m_occlusionKernelID, _SortingData, m_instancesSortingData);
            occlusionCS.SetBuffer(m_occlusionKernelID, _ShadowSortingData, m_instancesShadowSortingData);
            if (hiZBuffer != null) {
                occlusionCS.SetVector(_HiZTextureSize, hiZBuffer.TextureSize);
                occlusionCS.SetTexture(m_occlusionKernelID, _HiZMap, hiZBuffer.Texture);
            }

            occlusionCS.SetInt(_UseHiZ, hiZBuffer != null ? 1 : 0);

            copyInstanceDataCS.SetInt(_NumOfDrawcalls, m_numberOfInstanceTypes * NUMBER_OF_DRAW_CALLS);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, _InstanceDataBuffer, m_instanceDataBuffer);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, _InstancesDrawMatrixRows, m_instancesMatrixRows01);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, _InstancesDrawAnimData, m_instancesDrawAnimData);

            //CreateCommandBuffers();
        }

        private void InitPrefabBuffers(List<IndirectInstanceData> _instances) {
            bool isSrp = GraphicsSettings.renderPipelineAsset != null;
            m_numberOfInstanceTypes = _instances.Count;
            if (hiZBuffer != null) {
                hiZBuffer.Enabled = true;
                hiZBuffer.InitializeTexture();
            }

            indirectMeshes = new IndirectRenderingMesh[m_numberOfInstanceTypes];
            m_args = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
            for (int i = 0; i < m_numberOfInstanceTypes; i++) {
                IndirectRenderingMesh irm = new IndirectRenderingMesh();
                IndirectInstanceData iid = _instances[i];

                // Initialize Mesh
                irm.numOfVerticesLod00 = (uint)iid.lod00Mesh.vertexCount;
                irm.numOfVerticesLod01 = (uint)iid.lod01Mesh.vertexCount;
                irm.numOfVerticesLod02 = (uint)iid.lod02Mesh.vertexCount;
                irm.numOfIndicesLod00 = iid.lod00Mesh.GetIndexCount(0);
                irm.numOfIndicesLod01 = iid.lod01Mesh.GetIndexCount(0);
                irm.numOfIndicesLod02 = iid.lod02Mesh.GetIndexCount(0);

                irm.mesh = new Mesh();
                irm.mesh.name = iid.prefab.name;
                irm.mesh.CombineMeshes(
                    new CombineInstance[] {
                        new CombineInstance() { mesh = iid.lod00Mesh },
                        new CombineInstance() { mesh = iid.lod01Mesh },
                        new CombineInstance() { mesh = iid.lod02Mesh }
                    },
                    true, // Merge Submeshes 
                    false, // Use Matrices
                    false // Has lightmap data
                );

                // Arguments
                int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE_TYPE;

                // Buffer with arguments has to have five integer numbers
                // LOD00
                m_args[argsIndex + 0] = irm.numOfIndicesLod00; // 0 - index count per instance, 
                m_args[argsIndex + 1] = 0; // 1 - instance count
                m_args[argsIndex + 2] = 0; // 2 - start index location
                m_args[argsIndex + 3] = 0; // 3 - base vertex location
                m_args[argsIndex + 4] = 0; // 4 - start instance location

                // LOD01
                m_args[argsIndex + 5] = irm.numOfIndicesLod01; // 0 - index count per instance, 
                m_args[argsIndex + 6] = 0; // 1 - instance count
                m_args[argsIndex + 7] = m_args[argsIndex + 0] + m_args[argsIndex + 2]; // 2 - start index location
                m_args[argsIndex + 8] = 0; // 3 - base vertex location
                m_args[argsIndex + 9] = 0; // 4 - start instance location

                // LOD02
                m_args[argsIndex + 10] = irm.numOfIndicesLod02; // 0 - index count per instance, 
                m_args[argsIndex + 11] = 0; // 1 - instance count
                m_args[argsIndex + 12] = m_args[argsIndex + 5] + m_args[argsIndex + 7]; // 2 - start index location
                m_args[argsIndex + 13] = 0; // 3 - base vertex location
                m_args[argsIndex + 14] = 0; // 4 - start instance location

                // Materials
                irm.material = isSrp?iid.indirectMaterialSRP:iid.indirectMaterial; //new Material(iid.indirectMaterial);
                irm.originalBounds = CalculateBounds(iid.prefab);
                // Add the data to the renderer list
                indirectMeshes[i] = irm;
                _rendererData.prefabSize[i] = irm.originalBounds.size;
            }

            ReleasePrefabBuffer();
            m_instancesArgsBuffer = new ComputeBuffer(m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE,
                sizeof(uint),
                ComputeBufferType.IndirectArguments);
            m_shadowArgsBuffer = new ComputeBuffer(m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE,
                sizeof(uint),
                ComputeBufferType.IndirectArguments);

            m_instancesArgsBuffer.SetData(m_args);
            m_shadowArgsBuffer.SetData(m_args);
        }

        public void ReleaseBuffers() {
            ReleasePrefabBuffer();
            ReleaseInstanceBuffers();
        }

        private void ReleasePrefabBuffer() {
            ReleaseComputeBuffer(ref m_instancesArgsBuffer);
            ReleaseComputeBuffer(ref m_shadowArgsBuffer);
        }

        private void ReleaseInstanceBuffers() {
            //ReleaseCommandBuffer(ref visibleInstancesCB);

            ReleaseComputeBuffer(ref m_instancesIsVisibleBuffer);
            ReleaseComputeBuffer(ref m_instanceDataBuffer);
            ReleaseComputeBuffer(ref m_instancesSortingData);
            ReleaseComputeBuffer(ref m_instancesShadowSortingData);
            ReleaseComputeBuffer(ref m_instancesSortingDataTemp);
            ReleaseComputeBuffer(ref m_instancesMatrixRows01);
            ReleaseComputeBuffer(ref m_instancesCulledMatrixRows01);

            ReleaseComputeBuffer(ref m_shadowsIsVisibleBuffer);
            ReleaseComputeBuffer(ref m_shadowCulledMatrixRows01);

            ReleaseComputeBuffer(ref m_transformDataBuffer);

            ReleaseComputeBuffer(ref m_instancesDrawAnimData);
            ReleaseComputeBuffer(ref m_instancesCulledAnimData);
            ReleaseComputeBuffer(ref m_shadowCulledAnimData);

            ReleaseComputeBuffer(ref m_instancesDrawIndexRemap);
            ReleaseComputeBuffer(ref m_instancesCulledIndexRemap);
            ReleaseComputeBuffer(ref m_shadowCulledIndexRemap);
        }

        private static void ReleaseComputeBuffer(ref ComputeBuffer _buffer) {
            if (_buffer == null) {
                return;
            }

            _buffer.Release();
            _buffer = null;
        }

        private static void ReleaseCommandBuffer(ref CommandBuffer _buffer) {
            if (_buffer == null) {
                return;
            }

            _buffer.Release();
            _buffer = null;
        }

        private static bool TryGetKernel(string kernelName, ComputeShader cs, ref int kernelID) {
            if (!cs.HasKernel(kernelName)) {
                Debug.LogError(kernelName + " kernel not found in " + cs.name + "!");
                return false;
            }

            kernelID = cs.FindKernel(kernelName);
            return true;
        }

        private Bounds CalculateBounds(GameObject _prefab) {
            GameObject obj = GameObject.Instantiate(_prefab);
            obj.transform.position = Vector3.zero;
            obj.transform.rotation = Quaternion.Euler(Vector3.zero);
            obj.transform.localScale = Vector3.one;
            Renderer[] rends = obj.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds();
            if (rends.Length > 0) {
                b = new Bounds(rends[0].bounds.center, rends[0].bounds.size);
                for (int r = 1; r < rends.Length; r++) {
                    b.Encapsulate(rends[r].bounds);
                }
            }

            b.center = Vector3.zero;
            GameObject.DestroyImmediate(obj);

            return b;
        }

        public bool TryGetKernels() {
            return TryGetKernel("CSMain", createDrawDataBufferCS, ref m_createDrawDataBufferKernelID)
                   && TryGetKernel("BitonicSort_128", sortingCS, ref m_sorting_128_CSKernelID)
                   && TryGetKernel("BitonicSort_256", sortingCS, ref m_sorting_256_CSKernelID)
                   && TryGetKernel("BitonicSort_512", sortingCS, ref m_sorting_512_CSKernelID)
                   && TryGetKernel("MatrixTranspose_64", sortingCS, ref m_sortingTranspose_64_KernelID)
                   && TryGetKernel("CSMain", occlusionCS, ref m_occlusionKernelID)
                   && TryGetKernel("CSMain", copyInstanceDataCS, ref m_copyInstanceDataKernelID)
                ;
        }

        #region Debug & Logging

        public void UpdateDebug() {
            if (!Application.isPlaying) {
                return;
            }

            occlusionCS.SetInt(_ShouldFrustumCull, enableFrustumCulling ? 1 : 0);
            occlusionCS.SetInt(_ShouldOcclusionCull, enableOcclusionCulling ? 1 : 0);
            occlusionCS.SetInt(_ShouldDetailCull, enableDetailCulling ? 1 : 0);
            occlusionCS.SetInt(_ShouldLOD, enableLOD ? 1 : 0);
            occlusionCS.SetInt(_ShouldOnlyUseLOD02Shadows, enableOnlyLOD02Shadows ? 1 : 0);
            occlusionCS.SetFloat(_DetailCullingScreenPercentage, detailCullingPercentage);

            if (debugDrawLOD != m_debugLastDrawLOD) {
                m_debugLastDrawLOD = debugDrawLOD;

                if (debugDrawLOD) {
                    for (int i = 0; i < indirectMeshes.Length; i++) {
                        indirectMeshes[i].material.EnableKeyword(DEBUG_SHADER_LOD_KEYWORD);
                    }
                }
                else {
                    for (int i = 0; i < indirectMeshes.Length; i++) {
                        indirectMeshes[i].material.DisableKeyword(DEBUG_SHADER_LOD_KEYWORD);
                    }
                }
            }

            if (logDebugAll) {
            }

            UpdateDebugUI();
        }

        private void UpdateDebugUI() {
            if (!debugShowUI) {
                if (m_uiObj != null) {
                    GameObject.Destroy(m_uiObj);
                }

                return;
            }

            if (m_uiObj == null) {
                m_uiObj = GameObject.Instantiate(debugUIPrefab);
                m_uiObj.transform.parent = transform;
                m_uiText = m_uiObj.transform.GetComponentInChildren<Text>();
            }

            if (m_instancesArgsBuffer != null) {
                uint[] argsBuffer = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
                uint[] shadowArgsBuffer = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
                m_instancesArgsBuffer.GetData(argsBuffer);
                m_shadowArgsBuffer.GetData(shadowArgsBuffer);

                m_debugUIText.Length = 0;

                uint totalCount = 0;
                uint totalLod00Count = 0;
                uint totalLod01Count = 0;
                uint totalLod02Count = 0;

                uint totalShadowCount = 0;
                uint totalShadowLod00Count = 0;
                uint totalShadowLod01Count = 0;
                uint totalShadowLod02Count = 0;

                uint totalIndices = 0;
                uint totalLod00Indices = 0;
                uint totalLod01Indices = 0;
                uint totalLod02Indices = 0;

                uint totalShadowIndices = 0;
                uint totalShadowLod00Indices = 0;
                uint totalShadowLod01Indices = 0;
                uint totalShadowLod02Indices = 0;

                uint totalVertices = 0;
                uint totalLod00Vertices = 0;
                uint totalLod01Vertices = 0;
                uint totalLod02Vertices = 0;

                uint totalShadowVertices = 0;
                uint totalShadowLod00Vertices = 0;
                uint totalShadowLod01Vertices = 0;
                uint totalShadowLod02Vertices = 0;

                int instanceIndex = 0;
                uint normMultiplier = (uint)(drawInstances ? 1 : 0);
                uint shadowMultiplier =
                    (uint)(drawInstanceShadows && QualitySettings.shadows != ShadowQuality.Disable ? 1 : 0);
                int cascades = QualitySettings.shadowCascades;


                m_debugUIText.AppendLine(
                    $"<color=#ffffff>Name {Time.frameCount}".PadRight(32) //.Substring(0, 58)
                    + $"Instances".PadRight(25) //.Substring(0, 25)
                    + $"Shadow Instances".PadRight(25) //.Substring(0, 25)
                    + $"Vertices".PadRight(31) //.Substring(0, 25)
                    + $"Indices</color>"
                );

                for (int i = 0; i < argsBuffer.Length; i = i + NUMBER_OF_ARGS_PER_INSTANCE_TYPE) {
                    IndirectRenderingMesh irm = indirectMeshes[instanceIndex];

                    uint lod00Count = argsBuffer[i + 1] * normMultiplier;
                    uint lod01Count = argsBuffer[i + 6] * normMultiplier;
                    uint lod02Count = argsBuffer[i + 11] * normMultiplier;

                    uint lod00ShadowCount = shadowArgsBuffer[i + 1] * shadowMultiplier;
                    uint lod01ShadowCount = shadowArgsBuffer[i + 6] * shadowMultiplier;
                    uint lod02ShadowCount = shadowArgsBuffer[i + 11] * shadowMultiplier;

                    uint lod00Indices = argsBuffer[i + 0] * normMultiplier;
                    uint lod01Indices = argsBuffer[i + 5] * normMultiplier;
                    uint lod02Indices = argsBuffer[i + 10] * normMultiplier;

                    uint shadowLod00Indices = shadowArgsBuffer[i + 0] * shadowMultiplier;
                    uint shadowLod01Indices = shadowArgsBuffer[i + 5] * shadowMultiplier;
                    uint shadowLod02Indices = shadowArgsBuffer[i + 10] * shadowMultiplier;

                    uint lod00Vertices = irm.numOfVerticesLod00 * normMultiplier;
                    uint lod01Vertices = irm.numOfVerticesLod01 * normMultiplier;
                    uint lod02Vertices = irm.numOfVerticesLod02 * normMultiplier;

                    uint shadowLod00Vertices = irm.numOfVerticesLod00 * shadowMultiplier;
                    uint shadowLod01Vertices = irm.numOfVerticesLod01 * shadowMultiplier;
                    uint shadowLod02Vertices = irm.numOfVerticesLod02 * shadowMultiplier;

                    // Output...
                    string lod00VertColor = (lod00Vertices > 10000 ? DEBUG_UI_RED_COLOR : DEBUG_UI_WHITE_COLOR);
                    string lod01VertColor = (lod01Vertices > 5000 ? DEBUG_UI_RED_COLOR : DEBUG_UI_WHITE_COLOR);
                    string lod02VertColor = (lod02Vertices > 1000 ? DEBUG_UI_RED_COLOR : DEBUG_UI_WHITE_COLOR);

                    string lod00IndicesColor = (lod00Indices > (lod00Vertices * 3.33f)
                        ? DEBUG_UI_RED_COLOR
                        : DEBUG_UI_WHITE_COLOR);
                    string lod01IndicesColor = (lod01Indices > (lod01Vertices * 3.33f)
                        ? DEBUG_UI_RED_COLOR
                        : DEBUG_UI_WHITE_COLOR);
                    string lod02IndicesColor = (lod02Indices > (lod02Vertices * 3.33f)
                        ? DEBUG_UI_RED_COLOR
                        : DEBUG_UI_WHITE_COLOR);

                    m_debugUIText.AppendLine(
                        $"<b><color=#809fff>{instanceIndex}. {irm.mesh.name}".PadRight(200).Substring(0, 35) +
                        "</color></b>"
                        + $"({lod00Count}, {lod01Count}, {lod02Count})"
                            .PadRight(200).Substring(0, 25)
                        + $"({lod00ShadowCount},{lod01ShadowCount}, {lod02ShadowCount})"
                            .PadRight(200).Substring(0, 25)
                        + $"({lod00VertColor}{lod00Vertices,5}</color>, {lod01VertColor}{lod01Vertices,5}</color>, {lod02VertColor}{lod02Vertices,5})</color>"
                            .PadRight(200).Substring(0, 100)
                        + $"({lod00IndicesColor}{lod00Indices,5}</color>, {lod01IndicesColor}{lod01Indices,5}</color>, {lod02IndicesColor}{lod02Indices,5})</color>"
                            .PadRight(5)
                    );

                    // Total
                    uint sumCount = lod00Count + lod01Count + lod02Count;
                    uint sumShadowCount = lod00ShadowCount + lod01ShadowCount + lod02ShadowCount;

                    uint sumLod00Indices = lod00Count * lod00Indices;
                    uint sumLod01Indices = lod01Count * lod01Indices;
                    uint sumLod02Indices = lod02Count * lod02Indices;
                    uint sumIndices = sumLod00Indices + sumLod01Indices + sumLod02Indices;

                    uint sumShadowLod00Indices = lod00ShadowCount * shadowLod00Indices;
                    uint sumShadowLod01Indices = lod01ShadowCount * shadowLod01Indices;
                    uint sumShadowLod02Indices = lod02ShadowCount * shadowLod02Indices;
                    uint sumShadowIndices = sumShadowLod00Indices + sumShadowLod01Indices + sumShadowLod02Indices;

                    uint sumLod00Vertices = lod00Count * lod00Vertices;
                    uint sumLod01Vertices = lod01Count * lod01Vertices;
                    uint sumLod02Vertices = lod02Count * lod02Vertices;
                    uint sumVertices = sumLod00Vertices + sumLod01Vertices + sumLod02Vertices;

                    uint sumShadowLod00Vertices = lod00ShadowCount * shadowLod00Vertices;
                    uint sumShadowLod01Vertices = lod01ShadowCount * shadowLod01Vertices;
                    uint sumShadowLod02Vertices = lod02ShadowCount * shadowLod02Vertices;
                    uint sumShadowVertices = sumShadowLod00Vertices + sumShadowLod01Vertices + sumShadowLod02Vertices;

                    totalCount += sumCount;
                    totalLod00Count += lod00Count;
                    totalLod01Count += lod01Count;
                    totalLod02Count += lod02Count;

                    totalShadowCount += sumShadowCount;
                    totalShadowLod00Count += lod00ShadowCount;
                    totalShadowLod01Count += lod01ShadowCount;
                    totalShadowLod02Count += lod02ShadowCount;

                    totalIndices += sumIndices;
                    totalLod00Indices += sumLod00Indices;
                    totalLod01Indices += sumLod01Indices;
                    totalLod02Indices += sumLod02Indices;

                    totalShadowIndices += sumShadowIndices;
                    totalShadowLod00Indices += sumShadowLod00Indices;
                    totalShadowLod01Indices += sumShadowLod01Indices;
                    totalShadowLod02Indices += sumShadowLod02Indices;

                    totalVertices += sumVertices;
                    totalLod00Vertices += sumLod00Vertices;
                    totalLod01Vertices += sumLod01Vertices;
                    totalLod02Vertices += sumLod02Vertices;

                    totalShadowVertices += sumShadowVertices;
                    totalShadowLod00Vertices += sumShadowLod00Vertices;
                    totalShadowLod01Vertices += sumShadowLod01Vertices;
                    totalShadowLod02Vertices += sumShadowLod02Vertices;


                    instanceIndex++;
                }

                m_debugUIText.AppendLine();
                m_debugUIText.AppendLine("<b>Total</b>");
                m_debugUIText.AppendLine(
                    string.Format(
                        "Instances:".PadRight(10).Substring(0, 10) + " {0, 8} ({1, 8}, {2, 8}, {3, 8})",
                        totalCount,
                        totalLod00Count,
                        totalLod01Count,
                        totalLod02Count,
                        totalShadowCount
                    )
                );
                m_debugUIText.AppendLine(
                    string.Format(
                        "Vertices:".PadRight(10).Substring(0, 10) + " {0, 8} ({1, 8}, {2, 8}, {3, 8})",
                        totalVertices,
                        totalLod00Vertices,
                        totalLod01Vertices,
                        totalLod02Vertices
                    )
                );
                m_debugUIText.AppendLine(
                    string.Format(
                        "Indices:".PadRight(10).Substring(0, 10) + " {0, 8} ({1, 8}, {2, 8}, {3, 8})",
                        totalIndices,
                        totalLod00Indices,
                        totalLod01Indices,
                        totalLod02Indices
                    )
                );

                m_debugUIText.AppendLine();
                m_debugUIText.AppendLine("<b>Shadow</b>");
                m_debugUIText.AppendLine(
                    string.Format(
                        "Instances:".PadRight(10).Substring(0, 10) + " {0, 8} ({1, 8}, {2, 8}, {3, 8}) * " + cascades +
                        " Cascades"
                        + " ==> {4, 8} ({5, 8}, {6, 8}, {7, 8})",
                        totalShadowCount,
                        totalShadowLod00Count,
                        totalShadowLod01Count,
                        totalShadowLod02Count,
                        totalShadowCount * cascades,
                        totalShadowLod00Count * cascades,
                        totalShadowLod01Count * cascades,
                        totalShadowLod02Count * cascades
                    )
                );

                m_debugUIText.AppendLine(
                    string.Format(
                        "Vertices:".PadRight(10).Substring(0, 10) + " {0, 8} ({1, 8}, {2, 8}, {3, 8}) * " + cascades +
                        " Cascades"
                        + " ==> {4, 8} ({5, 8}, {6, 8}, {7, 8})",
                        totalShadowVertices,
                        totalShadowLod00Vertices,
                        totalShadowLod01Vertices,
                        totalShadowLod02Vertices,
                        totalShadowVertices * cascades,
                        totalShadowLod00Vertices * cascades,
                        totalShadowLod01Vertices * cascades,
                        totalShadowLod02Vertices * cascades
                    )
                );
                m_debugUIText.AppendLine(
                    string.Format(
                        "Indices:".PadRight(10).Substring(0, 10) + " {0, 8} ({1, 8}, {2, 8}, {3, 8}) * " + cascades +
                        " Cascades"
                        + " ==> {4, 8} ({5, 8}, {6, 8}, {7, 8})",
                        totalShadowIndices,
                        totalShadowLod00Indices,
                        totalShadowLod01Indices,
                        totalShadowLod02Indices,
                        totalShadowIndices * cascades,
                        totalShadowLod00Indices * cascades,
                        totalShadowLod01Indices * cascades,
                        totalShadowLod02Indices * cascades
                    )
                );

                m_uiText.text = m_debugUIText.ToString();
            }
        }


        private void LogSortingData(string prefix = "") {
            SortingData[] sortingData = new SortingData[m_numberOfInstances];
            m_instancesSortingData.GetData(sortingData);

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(prefix)) {
                sb.AppendLine(prefix);
            }

            uint lastDrawCallIndex = 0;
            for (int i = 0; i < sortingData.Length; i++) {
                uint drawCallIndex = (sortingData[i].drawCallInstanceIndex >> 16);
                uint instanceIndex = (sortingData[i].drawCallInstanceIndex) & 0xFFFF;
                if (i == 0) {
                    lastDrawCallIndex = drawCallIndex;
                }

                sb.AppendLine("(" + drawCallIndex + ") --> " + sortingData[i].distanceToCam + " instanceIndex:" +
                              instanceIndex);

                if (lastDrawCallIndex != drawCallIndex) {
                    Debug.Log(sb.ToString());
                    sb = new StringBuilder();
                    lastDrawCallIndex = drawCallIndex;
                }
            }

            Debug.Log(sb.ToString());
        }

        private void LogInstanceAnimation(string prefix = "") {
            AnimRenderData[] infos = new AnimRenderData[m_numberOfInstances];
            m_instancesDrawAnimData.GetData(infos);
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(prefix)) {
                sb.AppendLine(prefix);
            }

            for (int i = 0; i < Mathf.Min(infos.Length, 5); i++) {
                sb.Append("" + infos[i].AnimInfo0.z + ",");
            }

            sb.AppendLine();
            for (int i = 0; i < infos.Length; i++) {
                sb.AppendLine(
                    i + "\n" + infos[i].ToString() + "\n"
                );
            }

            Debug.Log(sb.ToString());
        }

        private string _lastStr;

        public void CheckLogBuffers() {
            if (logAllArgBuffer) {
                LogAllBuffers(logAllArgBufferCount);
            }
        }
        public void LogAllBuffers(int count = 5) {
            count = Mathf.Min(m_numberOfInstances, count);
            StringBuilder sb = new StringBuilder();
            {
                //sb.AppendLine("IndexCountPerInstance InstanceCount StartIndexLocation BaseVertexLocation StartInstanceLocation");
                uint[] instancesArgs = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
                m_instancesArgsBuffer.GetData(instancesArgs);
                for (int i = 0; i < instancesArgs.Length; i++) {
                    sb.Append(instancesArgs[i] + " ");
                    if ((i + 1) % 5 == 0) {
                        sb.AppendLine("");
                    }
                }
            }
            sb.AppendLine("======== positions ===========");
            {
                var datas = _rendererData.transformData;
                for (int i = 0; i < count; i++) {
                    sb.Append(i + ": " + datas[i] + " instId: " + _rendererData.sortingData[i].drawCallInstanceIndex +
                              "\n");
                }

                sb.AppendLine();
            }

            sb.AppendLine("======== VisibleBuffer ===========");
            {
                uint[] indexRemap = new uint[m_numberOfInstances];
                m_instancesIsVisibleBuffer.GetData(indexRemap);
                for (int i = 0; i < count; i++) {
                    sb.Append(indexRemap[i] + " ");
                }

                sb.AppendLine();
            }
            sb.AppendLine("======== SortedInstanceId ===========");
            {
                SortingData[] sortingData = new SortingData[m_numberOfInstances];
                m_instancesSortingData.GetData(sortingData);
                for (int i = 0; i < count; i++) {
                    uint instanceIndex = (sortingData[i].drawCallInstanceIndex) & 0xFFFF;
                    sb.Append(instanceIndex + " ");
                }

                sb.AppendLine();
            }
            sb.AppendLine("======== SortedShadowInstanceId ===========");
            {
                SortingData[] sortingData = new SortingData[m_numberOfInstances];
                m_instancesShadowSortingData.GetData(sortingData);
                for (int i = 0; i < count; i++) {
                    uint instanceIndex = (sortingData[i].drawCallInstanceIndex) & 0xFFFF;
                    sb.Append(instanceIndex + " ");
                }

                sb.AppendLine();
            }

            sb.AppendLine("======== IndexRemap ===========");
            {
                uint[] indexRemap = new uint[m_numberOfInstances];
                m_instancesCulledIndexRemap.GetData(indexRemap);
                for (int i = 0; i < count; i++) {
                    sb.Append(indexRemap[i] + " ");
                }

                sb.AppendLine();
            }
            sb.AppendLine("======== SortData ===========");
            {
                SortingData[] sortingData = new SortingData[m_numberOfInstances];
                m_instancesSortingData.GetData(sortingData);

                uint lastDrawCallIndex = 0;
                for (int i = 0; i < count; i++) {
                    sb.AppendLine(sortingData[i].ToString());
                }
            }
            sb.AppendLine("======== SortShadowData ===========");
            {
                SortingData[] sortingData = new SortingData[m_numberOfInstances];
                m_instancesShadowSortingData.GetData(sortingData);

                uint lastDrawCallIndex = 0;
                for (int i = 0; i < count; i++) {
                    sb.AppendLine(sortingData[i].ToString());
                }
            }
            sb.AppendLine("======== CulledMatrix ===========");
            {
                IndirectMatrix[] matrix1 = new IndirectMatrix[m_numberOfInstances];
                m_instancesCulledMatrixRows01.GetData(matrix1);

                for (int i = 0; i < count; i++) {
                    sb.AppendLine(
                        i + "\n"
                          + matrix1[i].row0 + "\n"
                          + matrix1[i].row1 + "\n"
                          + matrix1[i].row2 + "\n"
                          + "\n"
                    );
                }
            }
            sb.AppendLine("======== RawMatrix ===========");
            string prefix = "";
            {
                IndirectMatrix[] matrix1 = new IndirectMatrix[m_numberOfInstances];
                m_instancesMatrixRows01.GetData(matrix1);

                for (int i = 0; i < count; i++) {
                    sb.AppendLine(
                        i + "\n"
                          + matrix1[i].row0 + "\n"
                          + matrix1[i].row1 + "\n"
                          + matrix1[i].row2 + "\n"
                          + "\n"
                    );
                }
            }
            var str = sb.ToString();
            if (str != _lastStr) {
                _lastStr = str;
                Debug.Log(_lastStr);
            }
        }

        private void LogInstanceDrawMatrices(string prefix = "") {
            IndirectMatrix[] matrix1 = new IndirectMatrix[m_numberOfInstances];
            m_instancesMatrixRows01.GetData(matrix1);

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(prefix)) {
                sb.AppendLine(prefix);
            }

            for (int i = 0; i < matrix1.Length; i++) {
                sb.AppendLine(
                    i + "\n"
                      + matrix1[i].row0 + "\n"
                      + matrix1[i].row1 + "\n"
                      + matrix1[i].row2 + "\n"
                      + "\n"
                );
            }

            Debug.Log(sb.ToString());
        }


        private void LogArgsBuffers(string instancePrefix = "", string shadowPrefix = "") {
            uint[] instancesArgs = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
            uint[] shadowArgs = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
            m_instancesArgsBuffer.GetData(instancesArgs);
            m_shadowArgsBuffer.GetData(shadowArgs);

            StringBuilder instancesSB = new StringBuilder();
            StringBuilder shadowsSB = new StringBuilder();

            if (!string.IsNullOrEmpty(instancePrefix)) {
                instancesSB.AppendLine(instancePrefix);
            }

            if (!string.IsNullOrEmpty(shadowPrefix)) {
                shadowsSB.AppendLine(shadowPrefix);
            }

            instancesSB.AppendLine("");
            shadowsSB.AppendLine("");

            instancesSB.AppendLine(
                "IndexCountPerInstance InstanceCount StartIndexLocation BaseVertexLocation StartInstanceLocation");
            shadowsSB.AppendLine(
                "IndexCountPerInstance InstanceCount StartIndexLocation BaseVertexLocation StartInstanceLocation");

            int counter = 0;
            instancesSB.AppendLine(indirectMeshes[counter].mesh.name);
            shadowsSB.AppendLine(indirectMeshes[counter].mesh.name);
            for (int i = 0; i < instancesArgs.Length; i++) {
                instancesSB.Append(instancesArgs[i] + " ");
                shadowsSB.Append(shadowArgs[i] + " ");

                if ((i + 1) % 5 == 0) {
                    instancesSB.AppendLine("");
                    shadowsSB.AppendLine("");

                    if ((i + 1) < instancesArgs.Length
                        && (i + 1) % NUMBER_OF_ARGS_PER_INSTANCE_TYPE == 0) {
                        instancesSB.AppendLine("");
                        shadowsSB.AppendLine("");

                        counter++;
                        IndirectRenderingMesh irm = indirectMeshes[counter];
                        Mesh m = irm.mesh;
                        instancesSB.AppendLine(m.name);
                        shadowsSB.AppendLine(m.name);
                    }
                }
            }

            Debug.Log(instancesSB.ToString());
            Debug.Log(shadowsSB.ToString());
        }

        private void LogInstancesIsVisibleBuffers(string instancePrefix = "", string shadowPrefix = "") {
            uint[] instancesIsVisible = new uint[m_numberOfInstances];
            uint[] shadowsIsVisible = new uint[m_numberOfInstances];
            m_instancesIsVisibleBuffer.GetData(instancesIsVisible);
            m_shadowsIsVisibleBuffer.GetData(shadowsIsVisible);

            StringBuilder instancesSB = new StringBuilder();
            StringBuilder shadowsSB = new StringBuilder();

            if (!string.IsNullOrEmpty(instancePrefix)) {
                instancesSB.AppendLine(instancePrefix);
            }

            if (!string.IsNullOrEmpty(shadowPrefix)) {
                shadowsSB.AppendLine(shadowPrefix);
            }

            for (int i = 0; i < instancesIsVisible.Length; i++) {
                instancesSB.AppendLine(i + ": " + instancesIsVisible[i]);
                shadowsSB.AppendLine(i + ": " + shadowsIsVisible[i]);
            }

            Debug.Log(instancesSB.ToString());
            Debug.Log(shadowsSB.ToString());
        }


        private void LogCulledInstancesAnimData(string instancePrefix = "", string shadowPrefix = "") {
            IndirectMatrix[] instancesMatrix1 = new IndirectMatrix[m_numberOfInstances];
            m_instancesCulledMatrixRows01.GetData(instancesMatrix1);

            IndirectMatrix[] shadowsMatrix1 = new IndirectMatrix[m_numberOfInstances];
            m_shadowCulledMatrixRows01.GetData(shadowsMatrix1);

            StringBuilder instancesSB = new StringBuilder();
            StringBuilder shadowsSB = new StringBuilder();

            if (!string.IsNullOrEmpty(instancePrefix)) {
                instancesSB.AppendLine(instancePrefix);
            }

            if (!string.IsNullOrEmpty(shadowPrefix)) {
                shadowsSB.AppendLine(shadowPrefix);
            }

            for (int i = 0; i < instancesMatrix1.Length; i++) {
                instancesSB.AppendLine(
                    i + "\n"
                      + instancesMatrix1[i].row0 + "\n"
                      + instancesMatrix1[i].row1 + "\n"
                      + instancesMatrix1[i].row2 + "\n"
                      + "\n"
                );

                shadowsSB.AppendLine(
                    i + "\n"
                      + shadowsMatrix1[i].row0 + "\n"
                      + shadowsMatrix1[i].row1 + "\n"
                      + shadowsMatrix1[i].row2 + "\n"
                      + "\n"
                );
            }

            Debug.Log(instancesSB.ToString());
            Debug.Log(shadowsSB.ToString());
        }

        private void LogCulledInstancesDrawMatrices(string instancePrefix = "", string shadowPrefix = "") {
            IndirectMatrix[] instancesMatrix1 = new IndirectMatrix[m_numberOfInstances];
            m_instancesCulledMatrixRows01.GetData(instancesMatrix1);

            IndirectMatrix[] shadowsMatrix1 = new IndirectMatrix[m_numberOfInstances];
            m_shadowCulledMatrixRows01.GetData(shadowsMatrix1);

            StringBuilder instancesSB = new StringBuilder();
            StringBuilder shadowsSB = new StringBuilder();

            if (!string.IsNullOrEmpty(instancePrefix)) {
                instancesSB.AppendLine(instancePrefix);
            }

            if (!string.IsNullOrEmpty(shadowPrefix)) {
                shadowsSB.AppendLine(shadowPrefix);
            }

            for (int i = 0; i < instancesMatrix1.Length; i++) {
                instancesSB.AppendLine(
                    i + "\n"
                      + instancesMatrix1[i].row0 + "\n"
                      + instancesMatrix1[i].row1 + "\n"
                      + instancesMatrix1[i].row2 + "\n"
                      + "\n"
                );

                shadowsSB.AppendLine(
                    i + "\n"
                      + shadowsMatrix1[i].row0 + "\n"
                      + shadowsMatrix1[i].row1 + "\n"
                      + shadowsMatrix1[i].row2 + "\n"
                      + "\n"
                );
            }

            Debug.Log(instancesSB.ToString());
            Debug.Log(shadowsSB.ToString());
        }

        #endregion
    }
}