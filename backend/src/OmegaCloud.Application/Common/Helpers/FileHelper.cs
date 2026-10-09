using OmegaCloud.Application.Common.Exceptions;

namespace OmegaCloud.Application.Common.Helpers;

public static class FileHelper
{
    private static readonly char[] InvalidChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

    public static string ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("File name cannot be empty.");
        }

        var trimmed = name.Trim();

        if (trimmed == "." || trimmed == "..")
        {
            throw new ValidationException("Invalid file name.");
        }

        if (trimmed.IndexOfAny(InvalidChars) >= 0 || trimmed.Any(char.IsControl))
        {
            throw new ValidationException("File name contains invalid characters.");
        }

        var sanitized = Path.GetFileName(trimmed).Trim();

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized != trimmed)
        {
            throw new ValidationException("Invalid file name.");
        }

        if (sanitized.Length > 255)
        {
            throw new ValidationException("File name cannot exceed 255 characters.");
        }

        return sanitized;
    }

    public static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".zip" => "application/zip",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            _ => "application/octet-stream"
        };
    }
}
