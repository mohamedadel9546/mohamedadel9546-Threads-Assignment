using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Json.Shared;

public class JsonFileStore
{

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fiellocks = new();

    private readonly string _datadirectory = Path.Combine(AppContext.BaseDirectory, "Data");

    public async Task<List<T>> ReadAllAsync<T>(string filename, CancellationToken cancellation)
    {
        string filepath = Path.Combine(_datadirectory, filename);

        if (!File.Exists(filepath))
        {
            return new List<T>();
        }

        string json = await File.ReadAllTextAsync(filepath, Encoding.UTF8, cancellation);

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, _options) ?? new List<T>();
        }
        catch (JsonException js)
        {
            throw new DataFileCorruptedException(filename, js);
        }
    }

    public async Task WriteAllAsync<T>(string filename, List<T> items, CancellationToken cancellation)
    {
        string path = Path.Combine(_datadirectory, filename);

        SemaphoreSlim filelock = _fiellocks.GetOrAdd(
           path,
           _ => new(1, 1));
        await filelock.WaitAsync(cancellation);
        try
        {
            await WriteFileAtomicallyAsync(path, items, cancellation);
        }
        finally
        {
            filelock.Release();
        }
    }

    public async Task UpdateAllAsync<T>(string filename, Action<List<T>> update, CancellationToken cancellation)
    {
        string path = Path.Combine(_datadirectory, filename);
        SemaphoreSlim filelock = _fiellocks.GetOrAdd(
            path,
            _ => new(1, 1));
        await filelock.WaitAsync(cancellation);

        try
        {

            List<T> items = await ReadAllAsync<T>(filename, cancellation);
            update(items);
            await WriteFileAtomicallyAsync<T>(path, items, cancellation);
        }
        finally
        {
            filelock.Release();
        }

    }
    private async Task WriteFileAtomicallyAsync<T>(
    string filePath,
    List<T> items,
    CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_datadirectory);

        string tempPath = Path.Combine(
            _datadirectory,
            $"{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(items, _options);

            await File.WriteAllTextAsync(
                tempPath,
                json,
                Encoding.UTF8,
                cancellationToken);

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public List<T> ReadAll<T>(string filename)
    {
        string filepath = Path.Combine(_datadirectory, filename);
        if (!File.Exists(filepath))
        {
            return new List<T>();
        }
        try
        {
            string json = File.ReadAllText(filepath,Encoding.UTF8);
            return  JsonSerializer.Deserialize<List<T>>(json,_options) ?? new List<T>();
        }
        catch (JsonException js)
        {
            throw new DataFileCorruptedException(filename, js);
        }
        
    }

    public void WriteAll<T>(string filename,List<T>items)
    {
        string path = Path.Combine(_datadirectory, filename);

        SemaphoreSlim filelock = _fiellocks.GetOrAdd(
            path,
            _ => new(1, 1)
            );
        filelock.Wait();
        try 
        {
            WriteFileAtomically(path, items);
        }
        finally
        {
            filelock.Release();
        }
    }
    public void UpdateAll<T>(string filename,Action<List<T>>Update)
    {
        string filepath = Path.Combine(_datadirectory,filename);

        SemaphoreSlim filelock = _fiellocks.GetOrAdd(
            filepath,
            _=>new(1,1)
            );
        filelock.Wait();
        try
        {
            List<T> Items = ReadAll<T>(filename);
            Update(Items);
            WriteFileAtomically<T>(filepath, Items);
        }
        finally
        {
            filelock.Release();
        }
    }

    public void WriteFileAtomically<T>(string filePath, List<T> items
   )
    {
        string Temp = Path.Combine(_datadirectory,$"{Guid.NewGuid():N}.temp");

        try
        {
            string json = JsonSerializer.Serialize(items,_options);
            File.WriteAllText(Temp, json,Encoding.UTF8);
            File.Move(Temp, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(Temp))
            {
                File.Delete(Temp);
            }
        }
    }

}
