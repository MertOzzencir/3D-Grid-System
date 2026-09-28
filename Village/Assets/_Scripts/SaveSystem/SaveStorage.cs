using System.IO;
using UnityEngine;

// Kayıt dosyalarının diskteki işleri. Her "slot" ayrı bir dosya: save.json, blueprint_bases.json...
// Birden çok kayıt yuvası eklemek için sadece slot adını seçen bir arayüz gerekir; burası hazır.
public static class SaveStorage
{
    public static string PathFor(string slot) => Path.Combine(Application.persistentDataPath, slot + ".json");

    public static bool Exists(string slot) => File.Exists(PathFor(slot));

    // Önce geçici dosyaya yazar, sonra asıl dosyayla değiştirir: yazarken oyun çökerse eski kayıt sağlam kalır
    public static void Write(string slot, string json)
    {
        string path = PathFor(slot);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);

        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    public static bool TryRead(string slot, out string json)
    {
        string path = PathFor(slot);
        json = File.Exists(path) ? File.ReadAllText(path) : null;
        return json != null;
    }

    public static void Delete(string slot)
    {
        string path = PathFor(slot);
        if (File.Exists(path)) File.Delete(path);
    }
}
