using UnityEngine;

// Kedinin kakası: ağaç tohumu. Kedi balık yiyip sindirince yanına düşer (Cat). Sağ tıkla alınır; elde tutarken ağacın
// yarı saydam önizlemesi (PlacementGhost) mouse'un hücresinde gövdesi olacak şekilde görünür: ağacın bütün ayak izi boş
// ve altı dolu ise (HeldGridIndicator.CanHoldAt) yeşilimsi, değilse kırmızımsı. Sol tık eker: kaka yok olur, ağaç
// fidan olarak konur ve büyür (Tree.StartAsSapling). Geçici görünüm: üst üste üç kahverengi top.
// Balık gibi yığınlanır: tutarken aynı türden kakalar toplanır, her sol tık yığından bir tane eker.
// Ağaçlar hep aynı görünmesin diye her ekimin dönüşü rastgele (90° adımlı; önizleme de o dönüşte, ekince yenisi seçilir).
public class CatPoop : Carryable
{
    [Tooltip("Ekilince çıkacak ağacın verisi (Tree)")]
    [SerializeField] private GridPlaceableSO treeData;
    [Tooltip("Geçici görünümün rengi (model yokken)")]
    [SerializeField] private Color placeholderColor = new Color(0.42f, 0.27f, 0.14f);
    [Tooltip("Önizleme: ekilebilir / ekilemez yer rengi (A: saydamlık)")]
    [SerializeField] private Color validColor = new Color(0.45f, 1f, 0.55f, 0.4f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.35f, 0.3f, 0.4f);
    [Tooltip("Village/Placement Ghost. Referansla tutulur ki build'e girsin (build mode önizlemesi de bunu kullanır).")]
    [SerializeField] private Shader ghostShader;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private GridPlaceable treeProbe; // ağacın kapalı kopyası: ayak izi ve önizleme bununla
    private PlacementGhost ghost;
    private Material validMaterial, invalidMaterial;

    protected override bool Stackable => true;

    protected override void BuildPlaceholder(Transform parent)
    {
        Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, -0.4f, 0f), new Vector3(0.45f, 0.2f, 0.45f), placeholderColor);
        Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, -0.27f, 0f), new Vector3(0.32f, 0.16f, 0.32f), placeholderColor);
        Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, -0.16f, 0f), new Vector3(0.18f, 0.12f, 0.18f), placeholderColor);
    }

    public override void InteractContractBeginnig()
    {
        base.InteractContractBeginnig();
        CreatePreview();
    }

    public override void InteractContract(out bool success)
    {
        base.InteractContract(out success);
        UpdatePreview();
    }

    public override void ContractCancel()
    {
        base.ContractCancel();
        DestroyPreview();
    }

    // Sol tık: uygun yerse eker
    protected override bool UseWithoutTarget()
    {
        if (treeProbe == null || !TryGetPlantOrigin(out Vector3Int origin, out bool valid) || !valid) return false;

        var tree = (GridPlaceable)treeData.Spawn();
        tree.Rotation = treeProbe.Rotation;
        if (!GridManager.Instance.PlaceablePlaceOn(tree, origin))
        {
            Destroy(tree.gameObject);
            return false;
        }
        if (tree is Tree growing) growing.StartAsSapling();
        RandomizeRotation(); // sonraki ağaç başka yöne baksın
        UseOne(); // yığından bir tane; son taneyse kaka (ve önizleme) yok olur
        return true;
    }

    // Kakanın durduğu hücre ağacın gövdesi olur: ağacın origin'i, gövdesi (ayak izinin ortası) o hücreye gelecek şekilde
    private bool TryGetPlantOrigin(out Vector3Int origin, out bool valid)
    {
        Vector3Int trunk = Vector3Int.RoundToInt(transform.position);
        Vector3 center = treeProbe.GetFootprint().Center;
        origin = trunk - new Vector3Int(Mathf.RoundToInt(center.x), 0, Mathf.RoundToInt(center.z));
        valid = HeldGridIndicator.CanHoldAt(treeProbe, origin, true);
        return true;
    }

    private void CreatePreview()
    {
        if (treeData == null || treeProbe != null) return;
        treeProbe = (GridPlaceable)treeData.Spawn();
        treeProbe.gameObject.SetActive(false); // sadece şekli ve önizlemesi için; oyunda yok
        RandomizeRotation();
        Shader shader = ghostShader != null ? ghostShader : Shader.Find("Village/Placement Ghost");
        if (shader == null) return;
        validMaterial = new Material(shader) { name = "Plant Ghost Valid (generated)" };
        invalidMaterial = new Material(shader) { name = "Plant Ghost Invalid (generated)" };
        validMaterial.SetColor(ColorId, validColor);
        invalidMaterial.SetColor(ColorId, invalidColor);
        ghost = new PlacementGhost(treeProbe.gameObject, treeData.GetFootprint().FilledCells(), validMaterial, invalidMaterial); // hayalet dönüşsüz kurulur
    }

    // Ekilecek ağacın dönüşü (önizleme ve ayak izi de bununla)
    private void RandomizeRotation()
    {
        if (treeProbe != null) treeProbe.Rotation = (GridMaskRotator.Rotation)Random.Range(0, 4);
    }

    private void UpdatePreview()
    {
        if (ghost == null) return;
        // Kedinin üstündeyken (besleme hedefi değil ama) ya da hedef üstündeyken önizleme yok: sadece boş yerde
        if (Drag.CurrentTarget != null || !TryGetPlantOrigin(out Vector3Int origin, out bool valid))
        {
            ghost.HideFrom(0);
            return;
        }
        ghost.Show(0, origin, treeProbe.GridRotation, valid);
    }

    private void DestroyPreview()
    {
        ghost?.Destroy();
        ghost = null;
        if (treeProbe != null) Destroy(treeProbe.gameObject);
        treeProbe = null;
        if (validMaterial != null) Destroy(validMaterial);
        if (invalidMaterial != null) Destroy(invalidMaterial);
        validMaterial = invalidMaterial = null;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        DestroyPreview();
    }
}
