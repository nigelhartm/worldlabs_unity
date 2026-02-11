using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace WorldLabs.API
{
    /// <summary>
    /// Unity client for the WorldLabs Marble API.
    /// Provides easy access to all API endpoints for world generation.
    /// </summary>
    public class WorldLabsClient
    {
        #region Constants

        private const string DEFAULT_BASE_URL = "https://api.worldlabs.ai";
        private const string API_KEY_HEADER = "WLT-Api-Key";
        private const string API_KEY_ENV_VAR = "WORLDLABS_API_KEY";
        private const string CONTENT_TYPE = "application/json";
        private const bool DEBUG_RAW_RESPONSES = false;

        #endregion

        #region Properties

        /// <summary>
        /// The base URL for the API.
        /// </summary>
        public string BaseUrl { get; private set; }

        /// <summary>
        /// The API key used for authentication.
        /// </summary>
        public string ApiKey { get; private set; }

        /// <summary>
        /// Default timeout for API requests in seconds.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Whether the client has a valid API key configured.
        /// </summary>
        public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new WorldLabs client with API key loaded from .env file.
        /// </summary>
        public WorldLabsClient() : this(null, DEFAULT_BASE_URL)
        {
        }

        /// <summary>
        /// Creates a new WorldLabs client with the specified API key.
        /// </summary>
        /// <param name="apiKey">The API key for authentication. If null, loads from .env file.</param>
        /// <param name="baseUrl">Optional custom base URL.</param>
        public WorldLabsClient(string apiKey, string baseUrl = DEFAULT_BASE_URL)
        {
            BaseUrl = baseUrl.TrimEnd('/');

            if (string.IsNullOrEmpty(apiKey))
            {
                // Try to load from .env file
                ApiKey = EnvLoader.Get(API_KEY_ENV_VAR);
                if (string.IsNullOrEmpty(ApiKey))
                {
                    Debug.LogWarning($"[WorldLabsClient] No API key found. Set {API_KEY_ENV_VAR} in .env file or pass it to the constructor.");
                }
            }
            else
            {
                ApiKey = apiKey;
            }
        }

        #endregion

        #region Health Check

        /// <summary>
        /// Performs a health check on the API.
        /// </summary>
        public async Task<bool> HealthCheckAsync()
        {
            try
            {
                var result = await GetAsync<object>("/marble/v1/");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Performs a health check on the API (coroutine version).
        /// </summary>
        public IEnumerator HealthCheck(Action<bool> callback)
        {
            yield return GetCoroutine<object>("/marble/v1/", result => callback(result != null));
        }

        #endregion

        #region World Generation

        /// <summary>
        /// Starts world generation from a text prompt.
        /// </summary>
        /// <param name="textPrompt">The text description of the world to generate.</param>
        /// <param name="displayName">Optional display name for the world.</param>
        /// <param name="model">The model to use (default: Marble 0.1-plus).</param>
        /// <param name="tags">Optional tags for the world.</param>
        /// <param name="seed">Optional random seed for generation.</param>
        /// <param name="isPublic">Whether the world should be public.</param>
        public async Task<GenerateWorldResponse> GenerateWorldFromTextAsync(
            string textPrompt,
            string displayName = null,
            MarbleModel model = MarbleModel.Plus,
            List<string> tags = null,
            int? seed = null,
            bool isPublic = false)
        {
            var request = new WorldsGenerateRequest
            {
                world_prompt = TextPrompt.Create(textPrompt),
                display_name = displayName,
                model = GetModelString(model),
                tags = tags,
                seed = seed,
                permission = isPublic ? Permission.Public : Permission.Private
            };

            return await GenerateWorldAsync(request);
        }

        /// <summary>
        /// Starts world generation from an image URL.
        /// </summary>
        /// <param name="imageUrl">The URL of the image to use.</param>
        /// <param name="textPrompt">Optional text guidance.</param>
        /// <param name="isPano">Whether the image is a panorama.</param>
        /// <param name="displayName">Optional display name for the world.</param>
        /// <param name="model">The model to use.</param>
        public async Task<GenerateWorldResponse> GenerateWorldFromImageUrlAsync(
            string imageUrl,
            string textPrompt = null,
            bool? isPano = null,
            string displayName = null,
            MarbleModel model = MarbleModel.Plus)
        {
            var request = new WorldsGenerateRequest
            {
                world_prompt = ImagePrompt.FromUrl(imageUrl, textPrompt, isPano),
                display_name = displayName,
                model = GetModelString(model)
            };

            return await GenerateWorldAsync(request);
        }

        /// <summary>
        /// Starts world generation from a Texture2D.
        /// </summary>
        /// <param name="texture">The texture to use for world generation.</param>
        /// <param name="textPrompt">Optional text guidance.</param>
        /// <param name="isPano">Whether the image is a panorama.</param>
        /// <param name="displayName">Optional display name for the world.</param>
        /// <param name="model">The model to use.</param>
        public async Task<GenerateWorldResponse> GenerateWorldFromTextureAsync(
            Texture2D texture,
            string textPrompt = null,
            bool? isPano = null,
            string displayName = null,
            MarbleModel model = MarbleModel.Plus)
        {
            var request = new WorldsGenerateRequest
            {
                world_prompt = ImagePrompt.FromTexture(texture, "png", textPrompt, isPano),
                display_name = displayName,
                model = GetModelString(model)
            };

            return await GenerateWorldAsync(request);
        }

        /// <summary>
        /// Starts world generation from a video URL.
        /// </summary>
        /// <param name="videoUrl">The URL of the video to use.</param>
        /// <param name="textPrompt">Optional text guidance.</param>
        /// <param name="displayName">Optional display name for the world.</param>
        /// <param name="model">The model to use.</param>
        public async Task<GenerateWorldResponse> GenerateWorldFromVideoUrlAsync(
            string videoUrl,
            string textPrompt = null,
            string displayName = null,
            MarbleModel model = MarbleModel.Plus)
        {
            var request = new WorldsGenerateRequest
            {
                world_prompt = VideoPrompt.FromUrl(videoUrl, textPrompt),
                display_name = displayName,
                model = GetModelString(model)
            };

            return await GenerateWorldAsync(request);
        }

        /// <summary>
        /// Starts world generation with a custom request.
        /// </summary>
        /// <param name="request">The world generation request.</param>
        public async Task<GenerateWorldResponse> GenerateWorldAsync(WorldsGenerateRequest request)
        {
            return await PostAsync<GenerateWorldResponse>("/marble/v1/worlds:generate", request);
        }

        /// <summary>
        /// Starts world generation (coroutine version).
        /// </summary>
        public IEnumerator GenerateWorld(WorldsGenerateRequest request, Action<GenerateWorldResponse> callback)
        {
            yield return PostCoroutine<GenerateWorldResponse>("/marble/v1/worlds:generate", request, callback);
        }

        #endregion

        #region Operations

        /// <summary>
        /// Gets the status of an operation by ID.
        /// </summary>
        /// <param name="operationId">The operation identifier.</param>
        public async Task<GetOperationResponse> GetOperationAsync(string operationId)
        {
            return await GetAsync<GetOperationResponse>($"/marble/v1/operations/{operationId}");
        }

        /// <summary>
        /// Gets the status of an operation (coroutine version).
        /// </summary>
        public IEnumerator GetOperation(string operationId, Action<GetOperationResponse> callback)
        {
            yield return GetCoroutine<GetOperationResponse>($"/marble/v1/operations/{operationId}", callback);
        }

        /// <summary>
        /// Waits for an operation to complete, polling at the specified interval.
        /// </summary>
        /// <param name="operationId">The operation identifier.</param>
        /// <param name="pollIntervalSeconds">How often to poll (default: 5 seconds).</param>
        /// <param name="timeoutSeconds">Maximum time to wait (default: 300 seconds / 5 minutes).</param>
        /// <param name="onProgress">Optional callback for progress updates.</param>
        public async Task<GetOperationResponse> WaitForOperationAsync(
            string operationId,
            float pollIntervalSeconds = 5f,
            float timeoutSeconds = 300f,
            Action<GetOperationResponse> onProgress = null)
        {
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                var response = await GetOperationAsync(operationId);

                onProgress?.Invoke(response);

                if (response.done)
                {
                    return response;
                }

                if (response.error != null)
                {
                    throw new WorldLabsException($"Operation failed: {response.error.message}", response.error.code ?? 0);
                }

                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));
                elapsed += pollIntervalSeconds;
            }

            throw new TimeoutException($"Operation timed out after {timeoutSeconds} seconds");
        }

        /// <summary>
        /// Waits for an operation to complete (coroutine version).
        /// </summary>
        public IEnumerator WaitForOperation(
            string operationId,
            Action<GetOperationResponse> onComplete,
            Action<GetOperationResponse> onProgress = null,
            Action<Exception> onError = null,
            float pollIntervalSeconds = 5f,
            float timeoutSeconds = 300f)
        {
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                GetOperationResponse response = null;
                bool requestComplete = false;

                yield return GetCoroutine<GetOperationResponse>(
                    $"/marble/v1/operations/{operationId}",
                    r => { response = r; requestComplete = true; });

                if (!requestComplete || response == null)
                {
                    onError?.Invoke(new Exception("Failed to get operation status"));
                    yield break;
                }

                onProgress?.Invoke(response);

                if (response.done)
                {
                    onComplete?.Invoke(response);
                    yield break;
                }

                if (response.error != null)
                {
                    onError?.Invoke(new WorldLabsException($"Operation failed: {response.error.message}", response.error.code ?? 0));
                    yield break;
                }

                yield return new WaitForSeconds(pollIntervalSeconds);
                elapsed += pollIntervalSeconds;
            }

            onError?.Invoke(new TimeoutException($"Operation timed out after {timeoutSeconds} seconds"));
        }

        #endregion

        #region Worlds

        /// <summary>
        /// Gets a world by ID.
        /// </summary>
        /// <param name="worldId">The world identifier.</param>
        public async Task<World> GetWorldAsync(string worldId)
        {
            return await GetAsync<World>($"/marble/v1/worlds/{worldId}");
        }

        /// <summary>
        /// Gets a world by ID (coroutine version).
        /// </summary>
        public IEnumerator GetWorld(string worldId, Action<World> callback)
        {
            yield return GetCoroutine<World>($"/marble/v1/worlds/{worldId}", callback);
        }

        /// <summary>
        /// Lists worlds with optional filters.
        /// </summary>
        /// <param name="pageSize">Number of results per page (1-100).</param>
        /// <param name="pageToken">Pagination token from previous response.</param>
        /// <param name="status">Filter by status.</param>
        /// <param name="model">Filter by model.</param>
        /// <param name="tags">Filter by tags.</param>
        /// <param name="isPublic">Filter by visibility.</param>
        /// <param name="sortBy">Sort order.</param>
        public async Task<ListWorldsResponse> ListWorldsAsync(
            int pageSize = 20,
            string pageToken = null,
            WorldStatus? status = null,
            MarbleModel? model = null,
            List<string> tags = null,
            bool? isPublic = null,
            SortBy sortBy = SortBy.created_at,
            string createdAfter = null,
            string createdBefore = null)
        {
            var request = new ListWorldsRequest
            {
                page_size = pageSize,
                page_token = pageToken,
                status = status?.ToString(),
                model = model.HasValue ? GetModelString(model.Value) : null,
                tags = tags,
                is_public = isPublic,
                created_after = createdAfter,
                created_before = createdBefore,
                sort_by = sortBy.ToString(),
                nonce = Guid.NewGuid().ToString("N")
            };

            return await PostAsync<ListWorldsResponse>("/marble/v1/worlds:list", request);
        }

        /// <summary>
        /// Lists worlds (coroutine version).
        /// </summary>
        public IEnumerator ListWorlds(ListWorldsRequest request, Action<ListWorldsResponse> callback)
        {
            yield return PostCoroutine<ListWorldsResponse>("/marble/v1/worlds:list", request, callback);
        }

        #endregion

        #region Media Assets

        /// <summary>
        /// Prepares a media asset upload.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <param name="kind">The media type (image or video).</param>
        /// <param name="extension">Optional file extension without dot.</param>
        public async Task<MediaAssetPrepareUploadResponse> PrepareMediaAssetUploadAsync(
            string fileName,
            MediaAssetKind kind,
            string extension = null)
        {
            var request = new MediaAssetPrepareUploadRequest
            {
                file_name = fileName,
                kind = kind.ToString().ToLower(),
                extension = extension
            };

            return await PostAsync<MediaAssetPrepareUploadResponse>("/marble/v1/media-assets:prepare_upload", request);
        }

        /// <summary>
        /// Prepares a media asset upload (coroutine version).
        /// </summary>
        public IEnumerator PrepareMediaAssetUpload(MediaAssetPrepareUploadRequest request, Action<MediaAssetPrepareUploadResponse> callback)
        {
            yield return PostCoroutine<MediaAssetPrepareUploadResponse>("/marble/v1/media-assets:prepare_upload", request, callback);
        }

        /// <summary>
        /// Gets a media asset by ID.
        /// </summary>
        /// <param name="mediaAssetId">The media asset identifier.</param>
        public async Task<MediaAsset> GetMediaAssetAsync(string mediaAssetId)
        {
            return await GetAsync<MediaAsset>($"/marble/v1/media-assets/{mediaAssetId}");
        }

        /// <summary>
        /// Gets a media asset by ID (coroutine version).
        /// </summary>
        public IEnumerator GetMediaAsset(string mediaAssetId, Action<MediaAsset> callback)
        {
            yield return GetCoroutine<MediaAsset>($"/marble/v1/media-assets/{mediaAssetId}", callback);
        }

        /// <summary>
        /// Uploads a file to the prepared upload URL.
        /// </summary>
        /// <param name="uploadInfo">The upload URL info from PrepareMediaAssetUploadAsync.</param>
        /// <param name="data">The file data to upload.</param>
        /// <param name="contentType">The content type (e.g., "image/png", "video/mp4").</param>
        public async Task<bool> UploadMediaAssetAsync(UploadUrlInfo uploadInfo, byte[] data, string contentType)
        {
            using (UnityWebRequest request = new UnityWebRequest(uploadInfo.upload_url, uploadInfo.upload_method))
            {
                request.uploadHandler = new UploadHandlerRaw(data);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", contentType);

                if (uploadInfo.required_headers != null)
                {
                    foreach (var header in uploadInfo.required_headers)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }

                request.useHttpContinue = false;
                request.timeout = TimeoutSeconds;

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[WorldLabsClient] Upload failed: {request.error}");
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Uploads a file to the prepared upload URL (coroutine version).
        /// </summary>
        public IEnumerator UploadMediaAsset(UploadUrlInfo uploadInfo, byte[] data, string contentType, Action<bool> callback)
        {
            using (UnityWebRequest request = new UnityWebRequest(uploadInfo.upload_url, uploadInfo.upload_method))
            {
                request.uploadHandler = new UploadHandlerRaw(data);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", contentType);

                if (uploadInfo.required_headers != null)
                {
                    foreach (var header in uploadInfo.required_headers)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }

                request.useHttpContinue = false;
                request.timeout = TimeoutSeconds;

                yield return request.SendWebRequest();

                callback?.Invoke(request.result == UnityWebRequest.Result.Success);
            }
        }

        /// <summary>
        /// Helper method to upload an image and get its media asset ID.
        /// </summary>
        /// <param name="texture">The texture to upload.</param>
        /// <param name="fileName">The file name.</param>
        /// <returns>The media asset ID for use in world generation.</returns>
        public async Task<string> UploadTextureAsync(Texture2D texture, string fileName = "image.png")
        {
            string extension = fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg") ? "jpg" : "png";
            byte[] data = extension == "jpg" ? texture.EncodeToJPG() : texture.EncodeToPNG();
            string contentType = extension == "jpg" ? "image/jpeg" : "image/png";

            var prepareResponse = await PrepareMediaAssetUploadAsync(fileName, MediaAssetKind.Image, extension);

            bool uploadSuccess = await UploadMediaAssetAsync(prepareResponse.upload_info, data, contentType);
            if (!uploadSuccess)
            {
                throw new Exception("Failed to upload texture");
            }

            return prepareResponse.media_asset.media_asset_id;
        }

        #endregion

        #region Helper Methods

        private string GetModelString(MarbleModel model)
        {
            return model == MarbleModel.Mini ? "Marble 0.1-mini" : "Marble 0.1-plus";
        }

        #endregion

        #region HTTP Methods - Async

        private async Task<T> GetAsync<T>(string endpoint)
        {
            // Add unique cache-busting parameter to prevent client-side caching
            string cacheBuster = $"_nocache={Guid.NewGuid():N}";
            string separator = endpoint.Contains("?") ? "&" : "?";
            string fullEndpoint = BaseUrl + endpoint + separator + cacheBuster;

            using (UnityWebRequest request = UnityWebRequest.Get(fullEndpoint))
            {
                request.SetRequestHeader(API_KEY_HEADER, ApiKey);
                request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                request.SetRequestHeader("Pragma", "no-cache");
                request.useHttpContinue = false;
                request.timeout = TimeoutSeconds;

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                return HandleResponse<T>(request);
            }
        }

        private async Task<T> PostAsync<T>(string endpoint, object body)
        {
            string json = SerializeRequest(body);

            if (DEBUG_RAW_RESPONSES)
            {
                Debug.Log($"[WorldLabsClient] POST REQUEST BODY to {endpoint}:\n{json}");
            }

            // Add unique cache-busting parameter to prevent client-side caching
            string cacheBuster = $"_nocache={Guid.NewGuid():N}";
            string separator = endpoint.Contains("?") ? "&" : "?";
            string fullEndpoint = BaseUrl + endpoint + separator + cacheBuster;

            using (UnityWebRequest request = new UnityWebRequest(fullEndpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", CONTENT_TYPE);
                request.SetRequestHeader(API_KEY_HEADER, ApiKey);
                request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                request.SetRequestHeader("Pragma", "no-cache");
                request.timeout = TimeoutSeconds;
                request.useHttpContinue = false;

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                return HandleResponse<T>(request);
            }
        }

        #endregion

        #region HTTP Methods - Coroutines

        private IEnumerator GetCoroutine<T>(string endpoint, Action<T> callback)
        {
            // Add unique cache-busting parameter to prevent client-side caching
            string cacheBuster = $"_nocache={Guid.NewGuid():N}";
            string separator = endpoint.Contains("?") ? "&" : "?";
            string fullEndpoint = BaseUrl + endpoint + separator + cacheBuster;

            using (UnityWebRequest request = UnityWebRequest.Get(fullEndpoint))
            {
                request.SetRequestHeader(API_KEY_HEADER, ApiKey);
                request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                request.SetRequestHeader("Pragma", "no-cache");
                request.useHttpContinue = false;
                request.timeout = TimeoutSeconds;

                yield return request.SendWebRequest();

                try
                {
                    callback?.Invoke(HandleResponse<T>(request));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WorldLabsClient] Error: {ex.Message}");
                    callback?.Invoke(default);
                }
            }
        }

        private IEnumerator PostCoroutine<T>(string endpoint, object body, Action<T> callback)
        {
            string json = SerializeRequest(body);

            // Add unique cache-busting parameter to prevent client-side caching
            string cacheBuster = $"_nocache={Guid.NewGuid():N}";
            string separator = endpoint.Contains("?") ? "&" : "?";
            string fullEndpoint = BaseUrl + endpoint + separator + cacheBuster;

            using (UnityWebRequest request = new UnityWebRequest(fullEndpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", CONTENT_TYPE);
                request.SetRequestHeader(API_KEY_HEADER, ApiKey);
                request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                request.SetRequestHeader("Pragma", "no-cache");
                request.useHttpContinue = false;
                request.timeout = TimeoutSeconds;

                yield return request.SendWebRequest();

                try
                {
                    callback?.Invoke(HandleResponse<T>(request));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WorldLabsClient] Error: {ex.Message}");
                    callback?.Invoke(default);
                }
            }
        }

        #endregion

        #region Response Handling

        private T HandleResponse<T>(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorBody = request.downloadHandler?.text ?? "";
                Debug.LogError($"[WorldLabsClient] Request failed: {request.responseCode} - {request.error}\n{errorBody}");

                // Try to parse validation error
                if (!string.IsNullOrEmpty(errorBody))
                {
                    try
                    {
                        var validationError = JsonUtility.FromJson<HTTPValidationError>(errorBody);
                        if (validationError?.detail != null && validationError.detail.Count > 0)
                        {
                            string details = string.Join(", ", validationError.detail.ConvertAll(d => d.msg));
                            throw new WorldLabsException($"Validation error: {details}", (int)request.responseCode);
                        }
                    }
                    catch (Exception) { }
                }

                throw new WorldLabsException($"Request failed: {request.error}", (int)request.responseCode);
            }

            string responseBody = request.downloadHandler.text;

            if (DEBUG_RAW_RESPONSES)
            {
                Debug.Log($"[WorldLabsClient] RAW RESPONSE from server ({request.url}):\n{responseBody}");
            }

            if (typeof(T) == typeof(object))
            {
                return default;
            }

            try
            {
                T result = JsonUtility.FromJson<T>(responseBody);

                // Post-process to handle spz_urls Dictionary that JsonUtility can't deserialize
                PostProcessResponse(result, responseBody);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLabsClient] Failed to parse response: {ex.Message}\nBody: {responseBody}");
                throw;
            }
        }

        private void PostProcessResponse<T>(T result, string rawJson)
        {
            // Handle World responses
            if (result is World world)
            {
                WorldLabsJsonParser.PostProcessWorld(world, rawJson);
            }
            // Handle GetOperationResponse (contains World)
            else if (result is GetOperationResponse opResponse)
            {
                WorldLabsJsonParser.PostProcessOperationResponse(opResponse, rawJson);
            }
            // Handle ListWorldsResponse (contains list of World)
            else if (result is ListWorldsResponse listResponse)
            {
                WorldLabsJsonParser.PostProcessListWorldsResponse(listResponse, rawJson);
            }
        }

        private string SerializeRequest(object obj)
        {
            // Use custom serialization to handle polymorphic types properly
            return WorldLabsJsonSerializer.Serialize(obj);
        }

        #endregion
    }

    /// <summary>
    /// Exception thrown by WorldLabs API operations.
    /// </summary>
    public class WorldLabsException : Exception
    {
        public int StatusCode { get; }

        public WorldLabsException(string message, int statusCode = 0) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
