using FabBridge;
using FabBridge.UI;

public static class FabBridgeMenu
{
	[Menu( "Editor", "Fab Bridge/Open Fab Bridge" )]
	public static void OpenFabBridge()
	{
		Log.Info( "FabBridge: OpenFabBridge menu clicked" );

		// Check if widget already exists
		var widget = FabBridgeWidget.Instance;
		Log.Info( $"FabBridge: Widget instance is {(widget != null ? "found" : "null")}" );

		if ( widget != null && widget.IsValid )
		{
			Log.Info( "FabBridge: Focusing existing widget" );
			widget.Focus();
			widget.Show();
			EditorWindow.DockManager.RaiseDock( widget );
		}
		else
		{
			// Create the widget directly using DockManager
			Log.Info( "FabBridge: Creating new FabBridgeWidget via DockManager" );
			widget = EditorWindow.DockManager.Create<FabBridgeWidget>();
			Log.Info( $"FabBridge: Created widget: {widget != null}" );
		}
	}

	[Menu( "Editor", "Fab Bridge/About" )]
	public static void ShowAbout()
	{
		Log.Info( "FabBridge: About menu clicked" );
		EditorUtility.DisplayDialog(
			"Fab Bridge for s&box",
			"Imports assets from Epic Games Fab marketplace into s&box.\n\n" +
			"Configure Fab to use 'Custom (socket port)' export with port 24981.\n\n" +
			"Note: Fab assets require a subscription for non-Unreal Engine use."
		);
	}
}
