using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace DeepDenoiseClient.Utils.Converters
{
    public class HttpDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? mode = parameter as string;

            switch (mode)
            {
                case "Status":
                    if (value is int code)
                        return code switch
                        {
                            200 => "200 OK",
                            201 => "201 Created",
                            202 => "202 Accepted",
                            204 => "204 No Content",
                            400 => "400 Bad Request",
                            401 => "401 Unauthorized",
                            403 => "403 Forbidden",
                            404 => "404 Not Found",
                            409 => "409 Conflict",
                            500 => "500 Internal Server Error",
                            502 => "502 Bad Gateway",
                            503 => "503 Service Unavailable",
                            504 => "504 Gateway Timeout",
                            _ => code.ToString()
                        };
                    break;
                case "StatusColor":
                    if (value is int code2)
                        return code2 switch
                        {
                            >= 200 and < 300 => Brushes.Green,
                            >= 400 and < 500 => Brushes.Orange,
                            >= 500 => Brushes.Red,
                            _ => Brushes.Gray
                        };
                    break;
                case "MethodColor":
                    if (value is string method)
                        return method.ToUpper() switch
                        {
                            "GET" => Brushes.Blue,
                            "POST" => Brushes.DarkGreen,
                            "PUT" => Brushes.Orange,
                            "DELETE" => Brushes.Red,
                            _ => Brushes.Gray
                        };
                    break;
                    // 추가 변환 로직 필요시 여기에 case 추가
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
