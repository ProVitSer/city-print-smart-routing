using TCX.Configuration;

namespace CityPrintSmartRouting.Services;

/// <summary>
/// Предоставляет единственное подключение к 3CX (PhoneSystem).
/// PhoneSystem.Reset() можно вызывать только один раз — этот интерфейс
/// гарантирует, что все сервисы используют одно и то же соединение.
/// </summary>
public interface IPbxConnectionProvider
{
    bool IsConnected { get; }
    PhoneSystem? PhoneSystem { get; }
}
