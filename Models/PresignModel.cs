using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeepDenoiseClient.Models
{
    public record PresignResult(string Url, string Method = "GET");
}
