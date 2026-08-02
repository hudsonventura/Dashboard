using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace Dashboard.Services;

/// <summary>
/// Serviço responsável por gerenciar a memória/histórico da geração solar no SQLite via Entity Framework.
/// Hoje: faz insert ou update com as últimas informações obtidas.
/// Dias anteriores: leitura.
/// </summary>
public class SolarHistoryService
{
    private readonly Func<SolarDbContext> _contextFactory;

    public SolarHistoryService()
    {
        _contextFactory = () => new SolarDbContext();
        InitDatabase();
    }

    public SolarHistoryService(Func<SolarDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
        InitDatabase();
    }

    private void InitDatabase()
    {
        try
        {
            using var db = _contextFactory();
            db.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao inicializar banco de dados SQLite solar: {ex.Message}");
        }
    }

    /// <summary>
    /// Salva ou atualiza a foto das informações do dia de hoje no SQLite.
    /// </summary>
    public async Task SaveOrUpdateTodayRecordAsync(
        DateOnly date,
        InverterDetail detail,
        List<InverterDayPoint> dayPoints,
        string first6Kw,
        string last6Kw,
        string stateText)
    {
        string dateKey = date.ToString("yyyy-MM-dd");
        using var db = _contextFactory();

        var record = await db.SolarHistoryRecords.FirstOrDefaultAsync(r => r.Date == dateKey);
        bool isNew = record == null;

        record ??= new SolarHistoryRecord { Date = dateKey };

        record.EToday = detail.EToday;
        record.ETodayStr = detail.ETodayStr;
        record.Pac = detail.Pac;
        record.PacStr = detail.PacStr;
        record.IAc1 = detail.IAc1;
        record.IAc1Str = detail.IAc1Str;

        record.UPv1 = detail.UPv1;
        record.UPv1Str = detail.UPv1Str;
        record.IPv1 = detail.IPv1;
        record.IPv1Str = detail.IPv1Str;
        record.Pow1 = detail.Pow1;
        record.Pow1Str = detail.Pow1Str;

        record.UPv2 = detail.UPv2;
        record.UPv2Str = detail.UPv2Str;
        record.IPv2 = detail.IPv2;
        record.IPv2Str = detail.IPv2Str;
        record.Pow2 = detail.Pow2;
        record.Pow2Str = detail.Pow2Str;

        record.InverterTemperature = detail.InverterTemperature;
        record.InverterTemperatureUnit = detail.InverterTemperatureUnit;
        record.IgbtTemperature = GetIgbtTemperatureCelsius(detail);

        record.State = detail.State;
        record.StateText = stateText;
        record.LastUpdatedText = detail.TimeStr ?? DateTime.Now.ToString("HH:mm:ss");

        record.First6KwText = first6Kw;
        record.Last6KwText = last6Kw;

        record.DayPointsJson = JsonSerializer.Serialize(dayPoints);
        record.LastSyncedAt = DateTime.Now;

        if (isNew)
        {
            await db.SolarHistoryRecords.AddAsync(record);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Obtém o registro de um determinado dia (formato "yyyy-MM-dd").
    /// </summary>
    public async Task<SolarHistoryRecord?> GetRecordByDateAsync(string dateKey)
    {
        using var db = _contextFactory();
        return await db.SolarHistoryRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Date == dateKey);
    }

    /// <summary>
    /// Retorna a lista de datas gravadas no banco de dados, ordenadas de forma ascendente.
    /// </summary>
    public async Task<List<string>> GetStoredDatesAsync()
    {
        using var db = _contextFactory();
        return await db.SolarHistoryRecords
            .AsNoTracking()
            .Select(r => r.Date)
            .OrderBy(d => d)
            .ToListAsync();
    }

    private static double? GetIgbtTemperatureCelsius(InverterDetail detail)
    {
        if (detail.IgbtTemperatureLimit.HasValue && detail.IgbtTemperatureLimit.Value < 85 && detail.IgbtTemperatureLimit.Value > -50)
        {
            return detail.IgbtTemperatureLimit.Value;
        }

        return detail.InverterTemperature;
    }
}
