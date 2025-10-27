using System;
using System.IO;

namespace DeepDenoiseClient.Utils
{
    public static class PathUtil
    {
        public static string? GetFileNameFromUrlOrPath(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            // s3://bucket/key/dir/file.tif
            if (input.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                const string prefix = "s3://";
                var afterScheme = input.Substring(prefix.Length); // "bucket/key/dir/file.tif"
                var firstSlash = afterScheme.IndexOf('/');        // 구분: bucket 과 key...
                if (firstSlash < 0 || firstSlash + 1 >= afterScheme.Length) return null; // key 없음
                var key = afterScheme.Substring(firstSlash + 1);  // "key/dir/file.tif"
                var name = Path.GetFileName(key);                 // "file.tif" 또는 ""
                return string.IsNullOrEmpty(name) ? null : name;
            }

            // http(s)://... 인 경우
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                var name = Path.GetFileName(uri.AbsolutePath);    // 쿼리스트링은 제외됨
                return string.IsNullOrEmpty(name) ? null : name;
            }

            // 로컬 경로나 상대경로로 간주
            var fileName = Path.GetFileName(input);
            return string.IsNullOrEmpty(fileName) ? null : fileName;
        }
    }
}