using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Forms;

namespace MindLift.UI.Components;

/// <summary>
/// Profile photos for the web app. A photo needs the sign-in token, so it is fetched through the API client and kept
/// as a data address; each person is fetched once per session and everyone asking at the same time shares one request.
/// </summary>
public sealed class AvatarPhotos(HttpClient http)
{
    public const long MaxBytes = 3L * 1024 * 1024;

    private readonly Dictionary<Guid, Task<string?>> _cache = new();
    private readonly SemaphoreSlim _gate = new(4);

    /// <summary>Raised when a photo is added or removed, so every avatar on screen refreshes.</summary>
    public event Action<Guid>? Changed;

    public Task<string?> GetAsync(Guid userId)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(userId, out var task))
            {
                task = FetchAsync(userId);
                _cache[userId] = task;
            }

            return task;
        }
    }

    private async Task<string?> FetchAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            using var response = await http.GetAsync($"api/v1/avatars/{userId}");
            if (response.StatusCode != HttpStatusCode.OK) return null;
            var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Saves the signed-in person's photo. Returns an error message to show, or null when it worked.</summary>
    public async Task<string?> UploadAsync(Guid myUserId, IBrowserFile file)
    {
        if (file.Size > MaxBytes) return "Photos can be up to 3 MB.";
        if (file.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return "Use a JPG, PNG or WEBP photo.";

        try
        {
            using var content = new MultipartFormDataContent();
            var stream = new StreamContent(file.OpenReadStream(MaxBytes));
            stream.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(stream, "file", file.Name);

            using var response = await http.PutAsync("api/v1/avatars/me", content);
            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode == HttpStatusCode.BadRequest ? "That photo could not be used. Try a JPG, PNG or WEBP under 3 MB." : "The photo could not be saved. Try again.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return "The photo could not be saved. Check your connection and try again.";
        }

        Forget(myUserId);
        return null;
    }

    public async Task<bool> RemoveAsync(Guid myUserId)
    {
        try
        {
            using var response = await http.DeleteAsync("api/v1/avatars/me");
            if (!response.IsSuccessStatusCode) return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }

        Forget(myUserId);
        return true;
    }

    private void Forget(Guid userId)
    {
        lock (_cache) _cache.Remove(userId);
        Changed?.Invoke(userId);
    }
}
