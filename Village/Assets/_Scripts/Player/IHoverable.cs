// Elde bir şey yokken mouse üstüne gelince haberdar olan obje (önizleme, vurgulama vs.).
// InteractableController her kare mouse'un altındaki ilk collider'ın objesinde arar.
public interface IHoverable
{
    void OnHoverEnter();
    void OnHoverExit();
}
