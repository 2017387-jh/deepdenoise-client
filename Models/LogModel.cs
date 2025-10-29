using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeepDenoiseClient.Models
{
    public class LogModel
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Path { get; set; } = "";
        public double? ElapsedMs { get; set; }
        public int? HttpStatus { get; set; }
        public double? KiloBytes { get; set; }
        public string Message { get; set; } = "";
    }
}
