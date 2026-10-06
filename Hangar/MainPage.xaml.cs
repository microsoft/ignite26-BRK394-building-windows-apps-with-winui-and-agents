using System.Numerics;
using Hangar.Rendering;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Scenes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Hangar;

public sealed partial class MainPage : Page
{
    private AircraftScene? _scene;

    public MainPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var compositor = ElementCompositionPreview.GetElementVisual(SceneHost).Compositor;
            var root = compositor.CreateContainerVisual();
            root.Size = new Vector2(1600, 900);
            var sprite = compositor.CreateSpriteVisual();
            sprite.Size = new Vector2(60, 60);
            sprite.Offset = new Vector3(20, 20, 0);
            sprite.Brush = compositor.CreateColorBrush(Colors.Red);
            root.Children.InsertAtTop(sprite);

            var sv = SceneVisual.Create(compositor);
            sv.Size = new Vector2(1600, 900);
            var node = SceneNode.Create(compositor);
            var mb = new MeshBuilder();
            mb.Box(Vector3.Zero, new Vector3(1, 1, 1));
            var r = SceneMeshRendererComponent.Create(compositor);
            r.Mesh = mb.Build(compositor);
            var m = SceneMetallicRoughnessMaterial.Create(compositor);
            m.BaseColorFactor = new Vector4(0, 1, 0, 1);
            m.EmissiveFactor = new Vector3(0, 1, 0);
            m.IsDoubleSided = true;
            r.Material = m;
            node.Components.Add(r);
            node.Transform.Translation = new Vector3(400, 300, 0);
            node.Transform.Scale = new Vector3(200, 200, 1);
            
            var outer = SceneNode.Create(compositor); outer.Children.Add(node); sv.Root = outer; sv.Offset = new Vector3(300, 0, 0);
            root.Children.InsertAtTop(sv);
            ElementCompositionPreview.SetElementChildVisual(SceneHost, root);
        };
    }

    private void OnExplode(object sender, RoutedEventArgs e) => _scene?.SetExploded(ExplodeToggle.IsChecked == true);

    private void OnOrbit(object sender, RoutedEventArgs e) => _scene?.Orbit(60, 0);
}

