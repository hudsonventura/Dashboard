namespace SolisDashboard.Display;

public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Céu limpo",
        1 => "Principalmente limpo",
        2 => "Parcialmente nublado",
        3 => "Nublado",
        45 or 48 => "Neblina",
        51 or 53 or 55 => "Garoa",
        56 or 57 => "Garoa congelante",
        61 or 63 or 65 => "Chuva",
        66 or 67 => "Chuva congelante",
        71 or 73 or 75 => "Neve",
        77 => "Grãos de neve",
        80 or 81 or 82 => "Pancadas de chuva",
        85 or 86 => "Pancadas de neve",
        95 => "Tempestade",
        96 or 99 => "Tempestade com granizo",
        _ => $"Código {code}"
    };

    /// <summary>Chave de ícone SVG embutido (Assets/Weather).</summary>
    public static string IconKey(int code) => code switch
    {
        0 => "clear",
        1 => "mainly-clear",
        2 => "partly-cloudy",
        3 => "overcast",
        45 or 48 => "fog",
        51 or 53 or 55 or 56 or 57 => "drizzle",
        61 or 63 or 65 or 66 or 67 => "rain",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        80 or 81 or 82 => "showers",
        95 or 96 or 99 => "thunder",
        _ => "unknown"
    };

    public static string DescribeOpenWeather(int id) => id switch
    {
        >= 200 and < 300 => "Tempestade",
        >= 300 and < 400 => "Garoa",
        >= 500 and < 600 => "Chuva",
        >= 600 and < 700 => "Neve",
        >= 700 and < 800 => "Neblina",
        800 => "Céu limpo",
        801 => "Poucas nuvens",
        802 => "Parcialmente nublado",
        803 or 804 => "Nublado",
        _ => $"Código {id}"
    };

    public static string IconKeyFromOpenWeather(int id, string? icon) => id switch
    {
        >= 200 and < 300 => "thunder",
        >= 300 and < 400 => "drizzle",
        >= 500 and < 520 => "rain",
        >= 520 and < 600 => "showers",
        >= 600 and < 700 => "snow",
        >= 700 and < 800 => "fog",
        800 => "clear",
        801 => "mainly-clear",
        802 => "partly-cloudy",
        803 or 804 => "overcast",
        _ => "unknown"
    };
}
