using System.Text.Json.Serialization;

namespace DeepDenoiseClient.Models;

public sealed class InvokeRequestModel
{
    // API에서 기대하는 이름에 맞춰 snake_case로 직렬화
    [JsonPropertyName("model")] public string Model { get; set; } = "efficient";
    [JsonPropertyName("pixel_pitch")] public int PixelPitch { get; set; } = 140;
    [JsonPropertyName("type")] public string Type { get; set; } = "static";
    [JsonPropertyName("strength")] public int Strength { get; set; } = 20;
    [JsonPropertyName("width")] public int Width { get; set; } = 3072;
    [JsonPropertyName("height")] public int Height { get; set; } = 3072;
    [JsonPropertyName("using_bits")] public int UsingBits { get; set; } = 16;
    [JsonPropertyName("digital_offset")] public int DigitalOffset { get; set; } = 100;

    // Update value in runtime
    [JsonPropertyName("img_input_url")] public string? ImgInputUrl { get; set; } = "s3://ddn-in-bucket/user/static_demo_140um_madible_VD.tif";
    [JsonPropertyName("img_output_url")] public string? ImgOutputUrl { get; set; } = "s3://ddn-out-bucket/user/output_static_demo_140um_madible_VD.tif";
}
