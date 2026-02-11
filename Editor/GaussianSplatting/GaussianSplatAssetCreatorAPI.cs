// SPDX-License-Identifier: MIT

using System;
using System.IO;
using GaussianSplatting.Editor.Utils;
using GaussianSplatting.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GaussianSplatting.Editor
{
    /// <summary>
    /// Public API for creating GaussianSplatAsset programmatically.
    /// This exposes the core functionality of GaussianSplatAssetCreator for use by other tools.
    /// </summary>
    [BurstCompile]
    public static class GaussianSplatAssetCreatorAPI
    {
        private const string kProgressTitle = "Creating Gaussian Splat Asset";

        /// <summary>
        /// Creates a GaussianSplatAsset from an SPZ or PLY file.
        /// </summary>
        /// <param name="inputFilePath">Path to the input SPZ or PLY file</param>
        /// <param name="outputFolder">Output folder path (must be under Assets/)</param>
        /// <param name="baseName">Base name for the output asset files</param>
        /// <param name="formatPos">Position format</param>
        /// <param name="formatScale">Scale format</param>
        /// <param name="formatColor">Color format</param>
        /// <param name="formatSH">Spherical harmonics format</param>
        /// <param name="importCameras">Whether to import camera data if available</param>
        /// <returns>The created GaussianSplatAsset, or null if creation failed</returns>
        public static GaussianSplatAsset CreateAsset(
            string inputFilePath,
            string outputFolder,
            string baseName,
            GaussianSplatAsset.VectorFormat formatPos,
            GaussianSplatAsset.VectorFormat formatScale,
            GaussianSplatAsset.ColorFormat formatColor,
            GaussianSplatAsset.SHFormat formatSH,
            bool importCameras = false)
        {
            if (string.IsNullOrWhiteSpace(inputFilePath))
            {
                Debug.LogError("[GaussianSplatAssetCreatorAPI] Input file path is empty");
                return null;
            }

            if (!File.Exists(inputFilePath))
            {
                Debug.LogError($"[GaussianSplatAssetCreatorAPI] Input file not found: {inputFilePath}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputFolder) || !outputFolder.StartsWith("Assets/"))
            {
                Debug.LogError($"[GaussianSplatAssetCreatorAPI] Output folder must be within project Assets/, was '{outputFolder}'");
                return null;
            }

            Directory.CreateDirectory(outputFolder);

            try
            {
                return CreateAssetInternal(inputFilePath, outputFolder, baseName, formatPos, formatScale, formatColor, formatSH, importCameras);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GaussianSplatAssetCreatorAPI] Failed to create asset: {ex.Message}");
                EditorUtility.ClearProgressBar();
                return null;
            }
        }

        private static unsafe GaussianSplatAsset CreateAssetInternal(
            string inputFilePath,
            string outputFolder,
            string baseName,
            GaussianSplatAsset.VectorFormat formatPos,
            GaussianSplatAsset.VectorFormat formatScale,
            GaussianSplatAsset.ColorFormat formatColor,
            GaussianSplatAsset.SHFormat formatSH,
            bool importCameras)
        {
            EditorUtility.DisplayProgressBar(kProgressTitle, "Reading data files", 0.0f);

            GaussianSplatAsset.CameraInfo[] cameras = importCameras ? LoadJsonCamerasFile(inputFilePath) : null;

            NativeArray<InputSplatData> inputSplats = default;
            try
            {
                GaussianFileReader.ReadFile(inputFilePath, out inputSplats);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                throw new Exception($"Failed to read input file: {ex.Message}");
            }

            if (inputSplats.Length == 0)
            {
                EditorUtility.ClearProgressBar();
                throw new Exception("Input file contains no splat data");
            }

            try
            {
                float3 boundsMin, boundsMax;
                var boundsJob = new CalcBoundsJob
                {
                    m_BoundsMin = &boundsMin,
                    m_BoundsMax = &boundsMax,
                    m_SplatData = inputSplats
                };
                boundsJob.Schedule().Complete();

                EditorUtility.DisplayProgressBar(kProgressTitle, "Morton reordering", 0.05f);
                ReorderMorton(inputSplats, boundsMin, boundsMax);

                // cluster SHs if needed
                NativeArray<int> splatSHIndices = default;
                NativeArray<GaussianSplatAsset.SHTableItemFloat16> clusteredSHs = default;
                if (formatSH >= GaussianSplatAsset.SHFormat.Cluster64k)
                {
                    EditorUtility.DisplayProgressBar(kProgressTitle, "Cluster SHs", 0.2f);
                    ClusterSHs(inputSplats, formatSH, out clusteredSHs, out splatSHIndices);
                }

                EditorUtility.DisplayProgressBar(kProgressTitle, "Creating data objects", 0.7f);
                GaussianSplatAsset asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
                int2[] layerInfo = new int2[] { new int2(0, inputSplats.Length) };
                asset.Initialize(inputSplats.Length, formatPos, formatScale, formatColor, formatSH, boundsMin, boundsMax, cameras, layerInfo);
                asset.name = baseName;

                var dataHash = new Hash128((uint)asset.splatCount, (uint)asset.formatVersion, 0, 0);
                string pathChunk = $"{outputFolder}/{baseName}_chk.bytes";
                string pathPos = $"{outputFolder}/{baseName}_pos.bytes";
                string pathOther = $"{outputFolder}/{baseName}_oth.bytes";
                string pathCol = $"{outputFolder}/{baseName}_col.bytes";
                string pathSh = $"{outputFolder}/{baseName}_shs.bytes";

                bool useChunks = formatPos != GaussianSplatAsset.VectorFormat.Float32 ||
                                 formatScale != GaussianSplatAsset.VectorFormat.Float32 ||
                                 formatColor != GaussianSplatAsset.ColorFormat.Float32x4 ||
                                 formatSH != GaussianSplatAsset.SHFormat.Float32;

                if (useChunks)
                    CreateChunkData(inputSplats, pathChunk, ref dataHash);
                CreatePositionsData(inputSplats, pathPos, ref dataHash, formatPos);
                CreateOtherData(inputSplats, pathOther, ref dataHash, splatSHIndices, formatScale);
                CreateColorData(inputSplats, pathCol, ref dataHash, formatColor);
                CreateSHData(inputSplats, pathSh, ref dataHash, clusteredSHs, formatSH);
                asset.SetDataHash(dataHash);

                if (splatSHIndices.IsCreated) splatSHIndices.Dispose();
                if (clusteredSHs.IsCreated) clusteredSHs.Dispose();

                EditorUtility.DisplayProgressBar(kProgressTitle, "Initial texture import", 0.85f);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUncompressedImport);

                EditorUtility.DisplayProgressBar(kProgressTitle, "Setup data onto asset", 0.95f);
                asset.SetAssetFiles(
                    0,
                    useChunks ? AssetDatabase.LoadAssetAtPath<TextAsset>(pathChunk) : null,
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathPos),
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathOther),
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathCol),
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathSh));

                var assetPath = $"{outputFolder}/{baseName}.asset";
                var savedAsset = CreateOrReplaceAsset(asset, assetPath);

                EditorUtility.DisplayProgressBar(kProgressTitle, "Saving assets", 0.99f);
                AssetDatabase.SaveAssets();
                
                // Fix m_EditorClassIdentifier issue - Unity 6 adds namespace which breaks loading
                FixAssetEditorClassIdentifier(assetPath);
                
                EditorUtility.ClearProgressBar();

                return savedAsset;
            }
            finally
            {
                if (inputSplats.IsCreated) inputSplats.Dispose();
            }
        }

        private static T CreateOrReplaceAsset<T>(T asset, string path) where T : UnityEngine.Object
        {
            T result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (result == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                result = asset;
            }
            else
            {
                if (typeof(Mesh).IsAssignableFrom(typeof(T))) { (result as Mesh)?.Clear(); }
                EditorUtility.CopySerialized(asset, result);
            }
            return result;
        }

        /// <summary>
        /// Fixes the m_EditorClassIdentifier field in the asset file.
        /// Unity 6 adds the full namespace which breaks asset loading in some cases.
        /// </summary>
        private static void FixAssetEditorClassIdentifier(string assetPath)
        {
            try
            {
                string fullPath = System.IO.Path.GetFullPath(assetPath);
                if (!System.IO.File.Exists(fullPath))
                    return;

                string content = System.IO.File.ReadAllText(fullPath);
                
                // Replace any m_EditorClassIdentifier with a value to empty
                string pattern = @"m_EditorClassIdentifier: .+";
                string replacement = "m_EditorClassIdentifier: ";
                
                string fixedContent = System.Text.RegularExpressions.Regex.Replace(content, pattern, replacement);
                
                if (fixedContent != content)
                {
                    System.IO.File.WriteAllText(fullPath, fixedContent);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GaussianSplatAssetCreatorAPI] Failed to fix m_EditorClassIdentifier: {ex.Message}");
            }
        }

        #region Bounds Calculation

        [BurstCompile]
        private struct CalcBoundsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public unsafe float3* m_BoundsMin;
            [NativeDisableUnsafePtrRestriction] public unsafe float3* m_BoundsMax;
            [ReadOnly] public NativeArray<InputSplatData> m_SplatData;

            public unsafe void Execute()
            {
                float3 boundsMin = float.PositiveInfinity;
                float3 boundsMax = float.NegativeInfinity;

                for (int i = 0; i < m_SplatData.Length; ++i)
                {
                    float3 pos = m_SplatData[i].pos;
                    boundsMin = math.min(boundsMin, pos);
                    boundsMax = math.max(boundsMax, pos);
                }
                *m_BoundsMin = boundsMin;
                *m_BoundsMax = boundsMax;
            }
        }

        #endregion

        #region Morton Reordering

        [BurstCompile]
        private struct ReorderMortonJob : IJobParallelFor
        {
            const float kScaler = (float)((1 << 21) - 1);
            public float3 m_BoundsMin;
            public float3 m_InvBoundsSize;
            [ReadOnly] public NativeArray<InputSplatData> m_SplatData;
            public NativeArray<(ulong, int)> m_Order;

            public void Execute(int index)
            {
                float3 pos = ((float3)m_SplatData[index].pos - m_BoundsMin) * m_InvBoundsSize * kScaler;
                uint3 ipos = (uint3)pos;
                ulong code = GaussianUtils.MortonEncode3(ipos);
                m_Order[index] = (code, index);
            }
        }

        private struct OrderComparer : System.Collections.Generic.IComparer<(ulong, int)>
        {
            public int Compare((ulong, int) a, (ulong, int) b)
            {
                if (a.Item1 < b.Item1) return -1;
                if (a.Item1 > b.Item1) return +1;
                return a.Item2 - b.Item2;
            }
        }

        private static void ReorderMorton(NativeArray<InputSplatData> splatData, float3 boundsMin, float3 boundsMax)
        {
            ReorderMortonJob order = new ReorderMortonJob
            {
                m_SplatData = splatData,
                m_BoundsMin = boundsMin,
                m_InvBoundsSize = 1.0f / (boundsMax - boundsMin),
                m_Order = new NativeArray<(ulong, int)>(splatData.Length, Allocator.TempJob)
            };
            order.Schedule(splatData.Length, 4096).Complete();
            order.m_Order.Sort(new OrderComparer());

            NativeArray<InputSplatData> copy = new(order.m_SplatData, Allocator.TempJob);
            for (int i = 0; i < copy.Length; ++i)
                order.m_SplatData[i] = copy[order.m_Order[i].Item2];
            copy.Dispose();

            order.m_Order.Dispose();
        }

        #endregion

        #region SH Clustering

        [BurstCompile]
        private static unsafe void GatherSHs(int splatCount, InputSplatData* splatData, float* shData)
        {
            for (int i = 0; i < splatCount; ++i)
            {
                UnsafeUtility.MemCpy(shData, ((float*)splatData) + 9, 15 * 3 * sizeof(float));
                splatData++;
                shData += 15 * 3;
            }
        }

        [BurstCompile]
        private struct ConvertSHClustersJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> m_Input;
            public NativeArray<GaussianSplatAsset.SHTableItemFloat16> m_Output;

            public void Execute(int index)
            {
                var addr = index * 15;
                GaussianSplatAsset.SHTableItemFloat16 res;
                res.sh1 = new half3(m_Input[addr + 0]);
                res.sh2 = new half3(m_Input[addr + 1]);
                res.sh3 = new half3(m_Input[addr + 2]);
                res.sh4 = new half3(m_Input[addr + 3]);
                res.sh5 = new half3(m_Input[addr + 4]);
                res.sh6 = new half3(m_Input[addr + 5]);
                res.sh7 = new half3(m_Input[addr + 6]);
                res.sh8 = new half3(m_Input[addr + 7]);
                res.sh9 = new half3(m_Input[addr + 8]);
                res.shA = new half3(m_Input[addr + 9]);
                res.shB = new half3(m_Input[addr + 10]);
                res.shC = new half3(m_Input[addr + 11]);
                res.shD = new half3(m_Input[addr + 12]);
                res.shE = new half3(m_Input[addr + 13]);
                res.shF = new half3(m_Input[addr + 14]);
                res.shPadding = default;
                m_Output[index] = res;
            }
        }

        private static bool ClusterSHProgress(float val)
        {
            EditorUtility.DisplayProgressBar(kProgressTitle, $"Cluster SHs ({val:P0})", 0.2f + val * 0.5f);
            return true;
        }

        private static unsafe void ClusterSHs(NativeArray<InputSplatData> splatData, GaussianSplatAsset.SHFormat format, out NativeArray<GaussianSplatAsset.SHTableItemFloat16> shs, out NativeArray<int> shIndices)
        {
            shs = default;
            shIndices = default;

            int shCount = GaussianSplatAsset.GetSHCount(format, splatData.Length);
            if (shCount >= splatData.Length)
                return;

            const int kShDim = 15 * 3;
            const int kBatchSize = 2048;
            float passesOverData = format switch
            {
                GaussianSplatAsset.SHFormat.Cluster64k => 0.3f,
                GaussianSplatAsset.SHFormat.Cluster32k => 0.4f,
                GaussianSplatAsset.SHFormat.Cluster16k => 0.5f,
                GaussianSplatAsset.SHFormat.Cluster8k => 0.8f,
                GaussianSplatAsset.SHFormat.Cluster4k => 1.2f,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };

            NativeArray<float> shData = new(splatData.Length * kShDim, Allocator.Persistent);
            GatherSHs(splatData.Length, (InputSplatData*)splatData.GetUnsafeReadOnlyPtr(), (float*)shData.GetUnsafePtr());

            NativeArray<float> shMeans = new(shCount * kShDim, Allocator.Persistent);
            shIndices = new(splatData.Length, Allocator.Persistent);

            KMeansClustering.Calculate(kShDim, shData, kBatchSize, passesOverData, ClusterSHProgress, shMeans, shIndices);
            shData.Dispose();

            shs = new NativeArray<GaussianSplatAsset.SHTableItemFloat16>(shCount, Allocator.Persistent);

            ConvertSHClustersJob job = new ConvertSHClustersJob
            {
                m_Input = shMeans.Reinterpret<float3>(4),
                m_Output = shs
            };
            job.Schedule(shCount, 256).Complete();
            shMeans.Dispose();
        }

        #endregion

        #region Chunk Data

        [BurstCompile]
        private struct CalcChunkDataJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<InputSplatData> splatData;
            public NativeArray<GaussianSplatAsset.ChunkInfo> chunks;

            public void Execute(int chunkIdx)
            {
                float3 chunkMinpos = float.PositiveInfinity;
                float3 chunkMinscl = float.PositiveInfinity;
                float4 chunkMincol = float.PositiveInfinity;
                float3 chunkMinshs = float.PositiveInfinity;
                float3 chunkMaxpos = float.NegativeInfinity;
                float3 chunkMaxscl = float.NegativeInfinity;
                float4 chunkMaxcol = float.NegativeInfinity;
                float3 chunkMaxshs = float.NegativeInfinity;

                int splatBegin = math.min(chunkIdx * GaussianSplatAsset.kChunkSize, splatData.Length);
                int splatEnd = math.min((chunkIdx + 1) * GaussianSplatAsset.kChunkSize, splatData.Length);

                for (int i = splatBegin; i < splatEnd; ++i)
                {
                    InputSplatData s = splatData[i];
                    s.scale = math.pow(s.scale, 1.0f / 8.0f);
                    s.opacity = GaussianUtils.SquareCentered01(s.opacity);
                    splatData[i] = s;

                    chunkMinpos = math.min(chunkMinpos, s.pos);
                    chunkMinscl = math.min(chunkMinscl, s.scale);
                    chunkMincol = math.min(chunkMincol, new float4(s.dc0, s.opacity));
                    chunkMinshs = math.min(chunkMinshs, s.sh1);
                    chunkMinshs = math.min(chunkMinshs, s.sh2);
                    chunkMinshs = math.min(chunkMinshs, s.sh3);
                    chunkMinshs = math.min(chunkMinshs, s.sh4);
                    chunkMinshs = math.min(chunkMinshs, s.sh5);
                    chunkMinshs = math.min(chunkMinshs, s.sh6);
                    chunkMinshs = math.min(chunkMinshs, s.sh7);
                    chunkMinshs = math.min(chunkMinshs, s.sh8);
                    chunkMinshs = math.min(chunkMinshs, s.sh9);
                    chunkMinshs = math.min(chunkMinshs, s.shA);
                    chunkMinshs = math.min(chunkMinshs, s.shB);
                    chunkMinshs = math.min(chunkMinshs, s.shC);
                    chunkMinshs = math.min(chunkMinshs, s.shD);
                    chunkMinshs = math.min(chunkMinshs, s.shE);
                    chunkMinshs = math.min(chunkMinshs, s.shF);

                    chunkMaxpos = math.max(chunkMaxpos, s.pos);
                    chunkMaxscl = math.max(chunkMaxscl, s.scale);
                    chunkMaxcol = math.max(chunkMaxcol, new float4(s.dc0, s.opacity));
                    chunkMaxshs = math.max(chunkMaxshs, s.sh1);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh2);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh3);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh4);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh5);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh6);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh7);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh8);
                    chunkMaxshs = math.max(chunkMaxshs, s.sh9);
                    chunkMaxshs = math.max(chunkMaxshs, s.shA);
                    chunkMaxshs = math.max(chunkMaxshs, s.shB);
                    chunkMaxshs = math.max(chunkMaxshs, s.shC);
                    chunkMaxshs = math.max(chunkMaxshs, s.shD);
                    chunkMaxshs = math.max(chunkMaxshs, s.shE);
                    chunkMaxshs = math.max(chunkMaxshs, s.shF);
                }

                chunkMaxpos = math.max(chunkMaxpos, chunkMinpos + 1.0e-5f);
                chunkMaxscl = math.max(chunkMaxscl, chunkMinscl + 1.0e-5f);
                chunkMaxcol = math.max(chunkMaxcol, chunkMincol + 1.0e-5f);
                chunkMaxshs = math.max(chunkMaxshs, chunkMinshs + 1.0e-5f);

                GaussianSplatAsset.ChunkInfo info = default;
                info.posX = new float2(chunkMinpos.x, chunkMaxpos.x);
                info.posY = new float2(chunkMinpos.y, chunkMaxpos.y);
                info.posZ = new float2(chunkMinpos.z, chunkMaxpos.z);
                info.sclX = math.f32tof16(chunkMinscl.x) | (math.f32tof16(chunkMaxscl.x) << 16);
                info.sclY = math.f32tof16(chunkMinscl.y) | (math.f32tof16(chunkMaxscl.y) << 16);
                info.sclZ = math.f32tof16(chunkMinscl.z) | (math.f32tof16(chunkMaxscl.z) << 16);
                info.colR = math.f32tof16(chunkMincol.x) | (math.f32tof16(chunkMaxcol.x) << 16);
                info.colG = math.f32tof16(chunkMincol.y) | (math.f32tof16(chunkMaxcol.y) << 16);
                info.colB = math.f32tof16(chunkMincol.z) | (math.f32tof16(chunkMaxcol.z) << 16);
                info.colA = math.f32tof16(chunkMincol.w) | (math.f32tof16(chunkMaxcol.w) << 16);
                info.shR = math.f32tof16(chunkMinshs.x) | (math.f32tof16(chunkMaxshs.x) << 16);
                info.shG = math.f32tof16(chunkMinshs.y) | (math.f32tof16(chunkMaxshs.y) << 16);
                info.shB = math.f32tof16(chunkMinshs.z) | (math.f32tof16(chunkMaxshs.z) << 16);
                chunks[chunkIdx] = info;

                for (int i = splatBegin; i < splatEnd; ++i)
                {
                    InputSplatData s = splatData[i];
                    s.pos = ((float3)s.pos - chunkMinpos) / (chunkMaxpos - chunkMinpos);
                    s.scale = ((float3)s.scale - chunkMinscl) / (chunkMaxscl - chunkMinscl);
                    s.dc0 = ((float3)s.dc0 - chunkMincol.xyz) / (chunkMaxcol.xyz - chunkMincol.xyz);
                    s.opacity = (s.opacity - chunkMincol.w) / (chunkMaxcol.w - chunkMincol.w);
                    s.sh1 = ((float3)s.sh1 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh2 = ((float3)s.sh2 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh3 = ((float3)s.sh3 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh4 = ((float3)s.sh4 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh5 = ((float3)s.sh5 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh6 = ((float3)s.sh6 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh7 = ((float3)s.sh7 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh8 = ((float3)s.sh8 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.sh9 = ((float3)s.sh9 - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shA = ((float3)s.shA - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shB = ((float3)s.shB - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shC = ((float3)s.shC - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shD = ((float3)s.shD - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shE = ((float3)s.shE - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    s.shF = ((float3)s.shF - chunkMinshs) / (chunkMaxshs - chunkMinshs);
                    splatData[i] = s;
                }
            }
        }

        private static void CreateChunkData(NativeArray<InputSplatData> splatData, string filePath, ref Hash128 dataHash)
        {
            int chunkCount = (splatData.Length + GaussianSplatAsset.kChunkSize - 1) / GaussianSplatAsset.kChunkSize;
            CalcChunkDataJob job = new CalcChunkDataJob
            {
                splatData = splatData,
                chunks = new(chunkCount, Allocator.TempJob),
            };

            job.Schedule(chunkCount, 8).Complete();

            dataHash.Append(ref job.chunks);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(job.chunks.Reinterpret<byte>(UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()));

            job.chunks.Dispose();
        }

        #endregion

        #region Position Data

        private static ulong EncodeFloat3ToNorm16(float3 v)
        {
            return (ulong)(v.x * 65535.5f) | ((ulong)(v.y * 65535.5f) << 16) | ((ulong)(v.z * 65535.5f) << 32);
        }

        private static uint EncodeFloat3ToNorm11(float3 v)
        {
            return (uint)(v.x * 2047.5f) | ((uint)(v.y * 1023.5f) << 11) | ((uint)(v.z * 2047.5f) << 21);
        }

        private static ushort EncodeFloat3ToNorm655(float3 v)
        {
            return (ushort)((uint)(v.x * 63.5f) | ((uint)(v.y * 31.5f) << 6) | ((uint)(v.z * 31.5f) << 11));
        }

        private static ushort EncodeFloat3ToNorm565(float3 v)
        {
            return (ushort)((uint)(v.x * 31.5f) | ((uint)(v.y * 63.5f) << 5) | ((uint)(v.z * 31.5f) << 11));
        }

        private static uint EncodeQuatToNorm10(float4 v)
        {
            return (uint)(v.x * 1023.5f) | ((uint)(v.y * 1023.5f) << 10) | ((uint)(v.z * 1023.5f) << 20) | ((uint)(v.w * 3.5f) << 30);
        }

        private static unsafe void EmitEncodedVector(float3 v, byte* outputPtr, GaussianSplatAsset.VectorFormat format)
        {
            switch (format)
            {
                case GaussianSplatAsset.VectorFormat.Float32:
                    *(float*)outputPtr = v.x;
                    *(float*)(outputPtr + 4) = v.y;
                    *(float*)(outputPtr + 8) = v.z;
                    break;
                case GaussianSplatAsset.VectorFormat.Norm16:
                    {
                        ulong enc = EncodeFloat3ToNorm16(math.saturate(v));
                        *(uint*)outputPtr = (uint)enc;
                        *(ushort*)(outputPtr + 4) = (ushort)(enc >> 32);
                    }
                    break;
                case GaussianSplatAsset.VectorFormat.Norm11:
                    {
                        uint enc = EncodeFloat3ToNorm11(math.saturate(v));
                        *(uint*)outputPtr = enc;
                    }
                    break;
                case GaussianSplatAsset.VectorFormat.Norm6:
                    {
                        ushort enc = EncodeFloat3ToNorm655(math.saturate(v));
                        *(ushort*)outputPtr = enc;
                    }
                    break;
            }
        }

        [BurstCompile]
        private struct CreatePositionsDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            public GaussianSplatAsset.VectorFormat m_Format;
            public int m_FormatSize;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*)m_Output.GetUnsafePtr() + index * m_FormatSize;
                EmitEncodedVector(m_Input[index].pos, outputPtr, m_Format);
            }
        }

        private static int NextMultipleOf(int size, int multipleOf)
        {
            return (size + multipleOf - 1) / multipleOf * multipleOf;
        }

        private static void CreatePositionsData(NativeArray<InputSplatData> inputSplats, string filePath, ref Hash128 dataHash, GaussianSplatAsset.VectorFormat formatPos)
        {
            int dataLen = inputSplats.Length * GaussianSplatAsset.GetVectorSize(formatPos);
            dataLen = NextMultipleOf(dataLen, 8);
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);

            CreatePositionsDataJob job = new CreatePositionsDataJob
            {
                m_Input = inputSplats,
                m_Format = formatPos,
                m_FormatSize = GaussianSplatAsset.GetVectorSize(formatPos),
                m_Output = data
            };
            job.Schedule(inputSplats.Length, 8192).Complete();

            dataHash.Append(data);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(data);

            data.Dispose();
        }

        #endregion

        #region Other Data

        [BurstCompile]
        private struct CreateOtherDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            [NativeDisableContainerSafetyRestriction][ReadOnly] public NativeArray<int> m_SplatSHIndices;
            public GaussianSplatAsset.VectorFormat m_ScaleFormat;
            public int m_FormatSize;
            [NativeDisableParallelForRestriction] public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                byte* outputPtr = (byte*)m_Output.GetUnsafePtr() + index * m_FormatSize;

                Quaternion rotQ = m_Input[index].rot;
                float4 rot = new float4(rotQ.x, rotQ.y, rotQ.z, rotQ.w);
                uint enc = EncodeQuatToNorm10(rot);
                *(uint*)outputPtr = enc;
                outputPtr += 4;

                EmitEncodedVector(m_Input[index].scale, outputPtr, m_ScaleFormat);
                outputPtr += GaussianSplatAsset.GetVectorSize(m_ScaleFormat);

                if (m_SplatSHIndices.IsCreated)
                    *(ushort*)outputPtr = (ushort)m_SplatSHIndices[index];
            }
        }

        private static void CreateOtherData(NativeArray<InputSplatData> inputSplats, string filePath, ref Hash128 dataHash, NativeArray<int> splatSHIndices, GaussianSplatAsset.VectorFormat formatScale)
        {
            int formatSize = GaussianSplatAsset.GetOtherSizeNoSHIndex(formatScale);
            if (splatSHIndices.IsCreated)
                formatSize += 2;
            int dataLen = inputSplats.Length * formatSize;

            dataLen = NextMultipleOf(dataLen, 8);
            NativeArray<byte> data = new(dataLen, Allocator.TempJob);

            CreateOtherDataJob job = new CreateOtherDataJob
            {
                m_Input = inputSplats,
                m_SplatSHIndices = splatSHIndices,
                m_ScaleFormat = formatScale,
                m_FormatSize = formatSize,
                m_Output = data
            };
            job.Schedule(inputSplats.Length, 8192).Complete();

            dataHash.Append(data);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(data);

            data.Dispose();
        }

        #endregion

        #region Color Data

        /// <summary>
        /// Creates color data file in Float32x4 format.
        /// The color format parameter is stored in the asset metadata for runtime texture conversion.
        /// The file always contains float4 data that the renderer converts at load time.
        /// </summary>
        private static void CreateColorData(NativeArray<InputSplatData> inputSplats, string filePath, ref Hash128 dataHash, GaussianSplatAsset.ColorFormat formatColor)
        {
            NativeArray<float4> data = new(inputSplats.Length, Allocator.TempJob);

            CreateSimpleColorDataJob job = new CreateSimpleColorDataJob
            {
                m_Input = inputSplats,
                m_Output = data
            };
            job.Schedule(inputSplats.Length, 8192).Complete();

            dataHash.Append(data);
            dataHash.Append((int)formatColor);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(data.Reinterpret<byte>(16));

            data.Dispose();
        }

        [BurstCompile]
        private struct CreateSimpleColorDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<float4> m_Output;

            public void Execute(int index)
            {
                var splat = m_Input[index];
                m_Output[index] = new float4(splat.dc0.x, splat.dc0.y, splat.dc0.z, splat.opacity);
            }
        }

        #endregion

        #region SH Data

        [BurstCompile]
        private struct CreateSHDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<InputSplatData> m_Input;
            public GaussianSplatAsset.SHFormat m_Format;
            public NativeArray<byte> m_Output;

            public unsafe void Execute(int index)
            {
                var splat = m_Input[index];

                switch (m_Format)
                {
                    case GaussianSplatAsset.SHFormat.Float32:
                        {
                            GaussianSplatAsset.SHTableItemFloat32 res;
                            res.sh1 = splat.sh1;
                            res.sh2 = splat.sh2;
                            res.sh3 = splat.sh3;
                            res.sh4 = splat.sh4;
                            res.sh5 = splat.sh5;
                            res.sh6 = splat.sh6;
                            res.sh7 = splat.sh7;
                            res.sh8 = splat.sh8;
                            res.sh9 = splat.sh9;
                            res.shA = splat.shA;
                            res.shB = splat.shB;
                            res.shC = splat.shC;
                            res.shD = splat.shD;
                            res.shE = splat.shE;
                            res.shF = splat.shF;
                            res.shPadding = default;
                            ((GaussianSplatAsset.SHTableItemFloat32*)m_Output.GetUnsafePtr())[index] = res;
                        }
                        break;
                    case GaussianSplatAsset.SHFormat.Float16:
                        {
                            GaussianSplatAsset.SHTableItemFloat16 res;
                            res.sh1 = new half3(splat.sh1);
                            res.sh2 = new half3(splat.sh2);
                            res.sh3 = new half3(splat.sh3);
                            res.sh4 = new half3(splat.sh4);
                            res.sh5 = new half3(splat.sh5);
                            res.sh6 = new half3(splat.sh6);
                            res.sh7 = new half3(splat.sh7);
                            res.sh8 = new half3(splat.sh8);
                            res.sh9 = new half3(splat.sh9);
                            res.shA = new half3(splat.shA);
                            res.shB = new half3(splat.shB);
                            res.shC = new half3(splat.shC);
                            res.shD = new half3(splat.shD);
                            res.shE = new half3(splat.shE);
                            res.shF = new half3(splat.shF);
                            res.shPadding = default;
                            ((GaussianSplatAsset.SHTableItemFloat16*)m_Output.GetUnsafePtr())[index] = res;
                        }
                        break;
                    case GaussianSplatAsset.SHFormat.Norm11:
                        {
                            GaussianSplatAsset.SHTableItemNorm11 res;
                            res.sh1 = EncodeFloat3ToNorm11(splat.sh1);
                            res.sh2 = EncodeFloat3ToNorm11(splat.sh2);
                            res.sh3 = EncodeFloat3ToNorm11(splat.sh3);
                            res.sh4 = EncodeFloat3ToNorm11(splat.sh4);
                            res.sh5 = EncodeFloat3ToNorm11(splat.sh5);
                            res.sh6 = EncodeFloat3ToNorm11(splat.sh6);
                            res.sh7 = EncodeFloat3ToNorm11(splat.sh7);
                            res.sh8 = EncodeFloat3ToNorm11(splat.sh8);
                            res.sh9 = EncodeFloat3ToNorm11(splat.sh9);
                            res.shA = EncodeFloat3ToNorm11(splat.shA);
                            res.shB = EncodeFloat3ToNorm11(splat.shB);
                            res.shC = EncodeFloat3ToNorm11(splat.shC);
                            res.shD = EncodeFloat3ToNorm11(splat.shD);
                            res.shE = EncodeFloat3ToNorm11(splat.shE);
                            res.shF = EncodeFloat3ToNorm11(splat.shF);
                            ((GaussianSplatAsset.SHTableItemNorm11*)m_Output.GetUnsafePtr())[index] = res;
                        }
                        break;
                    case GaussianSplatAsset.SHFormat.Norm6:
                        {
                            GaussianSplatAsset.SHTableItemNorm6 res;
                            res.sh1 = EncodeFloat3ToNorm565(splat.sh1);
                            res.sh2 = EncodeFloat3ToNorm565(splat.sh2);
                            res.sh3 = EncodeFloat3ToNorm565(splat.sh3);
                            res.sh4 = EncodeFloat3ToNorm565(splat.sh4);
                            res.sh5 = EncodeFloat3ToNorm565(splat.sh5);
                            res.sh6 = EncodeFloat3ToNorm565(splat.sh6);
                            res.sh7 = EncodeFloat3ToNorm565(splat.sh7);
                            res.sh8 = EncodeFloat3ToNorm565(splat.sh8);
                            res.sh9 = EncodeFloat3ToNorm565(splat.sh9);
                            res.shA = EncodeFloat3ToNorm565(splat.shA);
                            res.shB = EncodeFloat3ToNorm565(splat.shB);
                            res.shC = EncodeFloat3ToNorm565(splat.shC);
                            res.shD = EncodeFloat3ToNorm565(splat.shD);
                            res.shE = EncodeFloat3ToNorm565(splat.shE);
                            res.shF = EncodeFloat3ToNorm565(splat.shF);
                            res.shPadding = default;
                            ((GaussianSplatAsset.SHTableItemNorm6*)m_Output.GetUnsafePtr())[index] = res;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private static void EmitSimpleDataFile<T>(NativeArray<T> data, string filePath, ref Hash128 dataHash) where T : unmanaged
        {
            dataHash.Append(data);
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(data.Reinterpret<byte>(UnsafeUtility.SizeOf<T>()));
        }

        private static void CreateSHData(NativeArray<InputSplatData> inputSplats, string filePath, ref Hash128 dataHash, NativeArray<GaussianSplatAsset.SHTableItemFloat16> clusteredSHs, GaussianSplatAsset.SHFormat formatSH)
        {
            if (clusteredSHs.IsCreated)
            {
                EmitSimpleDataFile(clusteredSHs, filePath, ref dataHash);
            }
            else
            {
                int dataLen = (int)GaussianSplatAsset.CalcSHDataSize(inputSplats.Length, formatSH);
                NativeArray<byte> data = new(dataLen, Allocator.TempJob);
                CreateSHDataJob job = new CreateSHDataJob
                {
                    m_Input = inputSplats,
                    m_Format = formatSH,
                    m_Output = data
                };
                job.Schedule(inputSplats.Length, 8192).Complete();
                EmitSimpleDataFile(data, filePath, ref dataHash);
                data.Dispose();
            }
        }

        #endregion

        #region Camera Loading

        private const string kCamerasJson = "cameras.json";

        private static GaussianSplatAsset.CameraInfo[] LoadJsonCamerasFile(string curPath)
        {
            string camerasPath;
            while (true)
            {
                var dir = Path.GetDirectoryName(curPath);
                if (!Directory.Exists(dir))
                    return null;
                camerasPath = $"{dir}/{kCamerasJson}";
                if (File.Exists(camerasPath))
                    break;
                curPath = dir;
            }

            if (!File.Exists(camerasPath))
                return null;

            string json = File.ReadAllText(camerasPath);
            var jsonCameras = JSONParser.FromJson<System.Collections.Generic.List<JsonCamera>>(json);
            if (jsonCameras == null || jsonCameras.Count == 0)
                return null;

            var result = new GaussianSplatAsset.CameraInfo[jsonCameras.Count];
            for (var camIndex = 0; camIndex < jsonCameras.Count; camIndex++)
            {
                var jsonCam = jsonCameras[camIndex];
                var pos = new Vector3(jsonCam.position[0], jsonCam.position[1], jsonCam.position[2]);
                var axisx = new Vector3(jsonCam.rotation[0][0], jsonCam.rotation[1][0], jsonCam.rotation[2][0]);
                var axisy = new Vector3(jsonCam.rotation[0][1], jsonCam.rotation[1][1], jsonCam.rotation[2][1]);
                var axisz = new Vector3(jsonCam.rotation[0][2], jsonCam.rotation[1][2], jsonCam.rotation[2][2]);

                axisy *= -1;
                axisz *= -1;

                var cam = new GaussianSplatAsset.CameraInfo
                {
                    pos = pos,
                    axisX = axisx,
                    axisY = axisy,
                    axisZ = axisz,
                    fov = 25
                };
                result[camIndex] = cam;
            }

            return result;
        }

        [Serializable]
        private class JsonCamera
        {
            public int id;
            public string img_name;
            public int width;
            public int height;
            public float[] position;
            public float[][] rotation;
            public float fx;
            public float fy;
        }

        #endregion
    }
}
