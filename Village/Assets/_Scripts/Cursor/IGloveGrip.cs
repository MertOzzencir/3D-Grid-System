using UnityEngine;

// Tutulurken eldivenin nereye ve nasıl oturacağını kendisi belirleyen, hiç yok edilmeyen obje (alet).
// Eldiven tutma süresince GripPoint'in child'ı olur: alet dönünce ya da sallanınca eldiven de onunla hareket eder.
// Yok edilebilecek objeler (odun vs.) bunu uygulamamalı: eldiven onunla birlikte silinir.
public interface IGloveGrip
{
    // Avucun oturacağı nokta ve yön, PalmContact ile aynı eksenler: Y avucun baktığı yön, Z parmak uçları.
    // Null ise eldiven normal tutma davranışını kullanır. Tutarken değişebilir (örn. kameraya göre yön seçimi);
    // eldiven her kare sorar ve değişince yeni noktaya kayar.
    Transform GripPoint { get; }

    // Kavrarken parmakların kıvrılması (0 = düz, 1 = tam kıvrık); parmaklar yüzey aramaz, buna gider
    float GripCurl { get; }
}
