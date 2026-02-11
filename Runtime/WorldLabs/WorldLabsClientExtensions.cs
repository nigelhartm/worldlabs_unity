using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WorldLabs.API
{
    /// <summary>
    /// Extension methods and helper utilities for the WorldLabs client.
    /// </summary>
    public static class WorldLabsClientExtensions
    {
        #region Cache Busting Helper
        
        /// <summary>
        /// Adds a cache-busting query parameter to a URL.
        /// </summary>
        private static string AddCacheBuster(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            string separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}_nocache={Guid.NewGuid():N}";
        }
        
        /// <summary>
        /// Configures a UnityWebRequest to bypass caching.
        /// </summary>
        private static void ConfigureNoCaching(UnityWebRequest request)
        {
            request.useHttpContinue = false;
            request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
            request.SetRequestHeader("Pragma", "no-cache");
        }
        
        #endregion

        #region Convenience Methods

        /// <summary>
        /// Generates a world from text and waits for completion.
        /// </summary>
        /// <param name="client">The WorldLabs client.</param>
        /// <param name="textPrompt">The text description of the world.</param>
        /// <param name="displayName">Optional display name.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <returns>The generated world.</returns>
        public static async Task<World> GenerateAndWaitAsync(
            this WorldLabsClient client,
            string textPrompt,
            string displayName = null,
            Action<float> onProgress = null)
        {
            var response = await client.GenerateWorldFromTextAsync(textPrompt, displayName);

            var result = await client.WaitForOperationAsync(
                response.operation_id,
                onProgress: op =>
                {
                    if (op.metadata?.progress != null)
                    {
                        onProgress?.Invoke(op.metadata.progress.Value);
                    }
                });

            if (result.error != null)
            {
                throw new WorldLabsException($"World generation failed: {result.error.message}", result.error.code ?? 0);
            }

            return result.response;
        }

        /// <summary>
        /// Generates a world from an image URL and waits for completion.
        /// </summary>
        public static async Task<World> GenerateFromImageAndWaitAsync(
            this WorldLabsClient client,
            string imageUrl,
            string textPrompt = null,
            bool? isPano = null,
            string displayName = null,
            Action<float> onProgress = null)
        {
            var response = await client.GenerateWorldFromImageUrlAsync(imageUrl, textPrompt, isPano, displayName);

            var result = await client.WaitForOperationAsync(
                response.operation_id,
                onProgress: op =>
                {
                    if (op.metadata?.progress != null)
                    {
                        onProgress?.Invoke(op.metadata.progress.Value);
                    }
                });

            if (result.error != null)
            {
                throw new WorldLabsException($"World generation failed: {result.error.message}", result.error.code ?? 0);
            }

            return result.response;
        }

        /// <summary>
        /// Generates a world from a texture and waits for completion.
        /// </summary>
        public static async Task<World> GenerateFromTextureAndWaitAsync(
            this WorldLabsClient client,
            Texture2D texture,
            string textPrompt = null,
            bool? isPano = null,
            string displayName = null,
            Action<float> onProgress = null)
        {
            var response = await client.GenerateWorldFromTextureAsync(texture, textPrompt, isPano, displayName);

            var result = await client.WaitForOperationAsync(
                response.operation_id,
                onProgress: op =>
                {
                    if (op.metadata?.progress != null)
                    {
                        onProgress?.Invoke(op.metadata.progress.Value);
                    }
                });

            if (result.error != null)
            {
                throw new WorldLabsException($"World generation failed: {result.error.message}", result.error.code ?? 0);
            }

            return result.response;
        }

        #endregion

        #region Asset Download Helpers

        /// <summary>
        /// Downloads the panorama image from a world's assets.
        /// </summary>
        public static async Task<Texture2D> DownloadPanoramaAsync(this WorldLabsClient client, World world)
        {
            if (world?.assets?.imagery?.pano_url == null)
            {
                throw new Exception("World does not have a panorama URL");
            }

            return await DownloadTextureAsync(world.assets.imagery.pano_url);
        }

        /// <summary>
        /// Downloads the thumbnail image from a world's assets.
        /// </summary>
        public static async Task<Texture2D> DownloadThumbnailAsync(this WorldLabsClient client, World world)
        {
            if (world?.assets?.thumbnail_url == null)
            {
                throw new Exception("World does not have a thumbnail URL");
            }

            return await DownloadTextureAsync(world.assets.thumbnail_url);
        }

        /// <summary>
        /// Downloads a texture from a URL.
        /// Note: WebP format is not natively supported by Unity. Use DownloadTextureWithFallbackAsync for WebP URLs.
        /// </summary>
        public static async Task<Texture2D> DownloadTextureAsync(string url)
        {
            string noCacheUrl = AddCacheBuster(url);
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(noCacheUrl))
            {
                ConfigureNoCaching(request);
                
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to download texture: {request.error}");
                }

                return DownloadHandlerTexture.GetContent(request);
            }
        }
        
        /// <summary>
        /// Downloads a texture from a URL with fallback support.
        /// If the primary URL is WebP (not supported by Unity), the fallback URL will be used instead.
        /// </summary>
        public static async Task<Texture2D> DownloadTextureWithFallbackAsync(string primaryUrl, string fallbackUrl)
        {
            // Check if primary URL is WebP - Unity doesn't support WebP natively
            bool isWebP = primaryUrl != null && primaryUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
            
            string urlToUse = isWebP && !string.IsNullOrEmpty(fallbackUrl) ? fallbackUrl : primaryUrl;
            
            if (string.IsNullOrEmpty(urlToUse))
            {
                throw new Exception("No valid URL provided for texture download");
            }
            
            return await DownloadTextureAsync(urlToUse);
        }

        /// <summary>
        /// Downloads a texture from a URL (coroutine version).
        /// </summary>
        public static IEnumerator DownloadTexture(string url, Action<Texture2D> callback, Action<string> onError = null)
        {
            string noCacheUrl = AddCacheBuster(url);
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(noCacheUrl))
            {
                ConfigureNoCaching(request);
                
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    callback?.Invoke(null);
                }
                else
                {
                    callback?.Invoke(DownloadHandlerTexture.GetContent(request));
                }
            }
        }

        /// <summary>
        /// Downloads binary data from a URL.
        /// </summary>
        public static async Task<byte[]> DownloadBinaryAsync(string url)
        {
            string noCacheUrl = AddCacheBuster(url);
            using (UnityWebRequest request = UnityWebRequest.Get(noCacheUrl))
            {
                ConfigureNoCaching(request);
                
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to download: {request.error}");
                }

                return request.downloadHandler.data;
            }
        }

        /// <summary>
        /// Downloads binary data from a URL (coroutine version).
        /// </summary>
        public static IEnumerator DownloadBinary(string url, Action<byte[]> callback, Action<string> onError = null)
        {
            string noCacheUrl = AddCacheBuster(url);
            using (UnityWebRequest request = UnityWebRequest.Get(noCacheUrl))
            {
                ConfigureNoCaching(request);
                
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    callback?.Invoke(null);
                }
                else
                {
                    callback?.Invoke(request.downloadHandler.data);
                }
            }
        }

        #endregion

        #region Pagination Helpers

        /// <summary>
        /// Fetches all worlds matching the filter criteria, automatically handling pagination.
        /// </summary>
        public static async Task<List<World>> ListAllWorldsAsync(
            this WorldLabsClient client,
            WorldStatus? status = null,
            MarbleModel? model = null,
            List<string> tags = null,
            bool? isPublic = null,
            int maxResults = 1000)
        {
            var allWorlds = new List<World>();
            string pageToken = null;

            while (allWorlds.Count < maxResults)
            {
                var response = await client.ListWorldsAsync(
                    pageSize: Math.Min(100, maxResults - allWorlds.Count),
                    pageToken: pageToken,
                    status: status,
                    model: model,
                    tags: tags,
                    isPublic: isPublic);

                if (response.worlds != null)
                {
                    allWorlds.AddRange(response.worlds);
                }

                if (string.IsNullOrEmpty(response.next_page_token))
                {
                    break;
                }

                pageToken = response.next_page_token;
            }

            return allWorlds;
        }

        #endregion
    }
}
