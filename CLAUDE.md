# 3D Grid System (Village)

Unity 6000.3.11f1 projesi `Village/` klasöründe. Oyun kodu `Village/Assets/_Scripts`, editör kodu `Village/Assets/Editor`.
Kullanıcıyla Türkçe konuşulur; kod yorumları Türkçe.

## Çalışma şekli
- Büyük değişikliklerden önce tasarım konuşulur; kullanıcı "kodu değiştirme" dediyse sadece anlat.
- Açıklamalar sistem düzeyinde (class/metod isimleriyle) tercih edilir.
- Değişiklik sonrası derleme kontrol edilir; Unity'de test kullanıcıdadır.

## Temel kurallar (koddan kolay çıkmayanlar)
- **Grid:** 1 birim = 1 hücre. Her hücrede iki bağımsız slot var: `Base` ve `Placeable`. Slotlar birbirini bloklamaz (henüz karar verilmedi).
- **Pivot:** Her modelin pivot'u origin hücresinin ortasında. `transform.position` = `OriginWorldPosition` = pivot hücresi; bunu değiştirecek bir şey yapma (drag, Tree, blueprint buna güveniyor).
- **Footprint:** `GridFootprint` hücreleri pivot'a göre offset olarak tutar (negatif olabilir). SO'da katman katman tasarlanır (`GridEntitySOBase.layers`, `layers[0]` zemin).
- **Rotasyon:** Sadece Y ekseninde 90° adımlar. `GridMaskRotator.RotateOffset` ile `ToQuaternion` aynı matematik; görsel ve grid asla ayrışmamalı. Sadece elde tutulan obje döner (R); grid'deki objenin rotasyonu değiştirilmez (`PlacedFootprint` dondurulmuş kopya).
- **R tuşu:** elde obje varsa objeyi, yoksa kamerayı döndürür.
- **Taşıma:** `InteractableController` (ne zaman) → `IInteractable` → `GridDragMotor` (nasıl) → `IToolTarget` (hedefte ne olur). Yeni hedef türü = `IToolTarget` uygula.
- **Obje yok edilecekse** `InteractableController.Release` kullan, `HardCancel` değil (o, objeyi grid'e geri koymaya çalışır).

## Odun
- Odunlar **dik** durur. Uzunluk = SO'daki yükseklik (`Size.y`); `Lenghts` enum'u odun için kullanılmıyor (sadece ağaç okları).
- Katmanlar (`WoodLayout`): katman 0 = tepe → toplam uzunlukta tek parça prefab (`WoodCatalogSO`); katman 1..L = sağ yan, yukarıdan aşağı → parça 90° yatırılıp sağa uzanır, sonuç `MergedWood`.
- "Sağ" = odunun kendi sağı (rotasyonla döner). Sol taraf ve alt uç yok.
- Bir odun sadece bir kez yandan birleşir; `MergedWood` hedef olamaz ama taşınır ve döner.
- Kombinasyon prefab'ı yapılmaz (eski 2BR_1BR sistemi kaldırıldı).

## Blueprint (boyama kitabı)
- Blueprint sahnesinde (`Scenes/Blueprint.unity`) Play mode'da parçalar yerleştirilir, **Tools → Blueprint Creator** ile prefab + `GridPlaceableSO` üretilir (`Assets/Blueprints`).
- Pivot dünya (0,0,0); builder slot'ları 1 birim aşağı kaydırıyor (kullanıcının ayarı).
- Uyum `IBlueprintPiece.BlueprintSignature` ile (örn. `Wood:2`, `MergedWood:2:0,1,0`). Yeni parça türü (taş, çit) sadece bu interface'i uygular; sistem odun üzerine kurulmamalı.
- Sıradaki adım: slot'ları `IToolTarget` yapıp oyunda doldurma; boş slot görseli (shader) sonra.

## Ağaç kesme
- `ToolBase`: cooldown + `ToolSwingAnimation`; kullanım vuruş anında olur. Animasyon sadece `VisualTransform`'u döndürür (root motora ait).
- `Tree`: her vuruşta `VfxPool` ile talaş efekti (gövde yüzeyinde, +Z dışarı) ve `HitShake`; ok `maxHealth` bitince kesilir.
- VFX referansları prefab'da tutulur, ortak SO'larda değil. Efektler `VfxPool` ile oynatılır, Instantiate edilmez.

## Henüz yapılmayanlar
- Save sistemi: konuşuldu (JSON, `Application.persistentDataPath`, SO ID + registry), yazılmadı. `GridData.IsOrigin` ve eski `GridSaveManager` o zamana kadar duruyor.
