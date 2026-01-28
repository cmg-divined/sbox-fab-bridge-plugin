using System;

namespace FabBridge.UI;

/// <summary>
/// Main editor widget for FabBridge - provides UI for configuration and status
/// </summary>
[Dock( "Editor", "Fab Bridge", "download" )]
public class FabBridgeWidget : Widget
{
	private static FabBridgeWidget _instance;
	public static FabBridgeWidget Instance => _instance;

	private FabBridgeServer _server;
	private FabImportHandler _importHandler;

	// UI Elements
	private Label _statusLabel;
	private Button _startButton;
	private Button _stopButton;
	private LineEdit _portEdit;
	private ListView _importHistoryList;
	private LineEdit _importFolderEdit;
	private Checkbox _createMaterialsCheck;
	private Checkbox _convertModelsCheck;

	// Current port value
	private int _currentPort = FabBridgeServer.DefaultPort;

	// Import history
	private List<FabImportHandler.ImportResult> _importHistory = new();

	public FabBridgeWidget( Widget parent ) : base( parent )
	{
		Log.Info( "FabBridge: Widget constructor called" );
		_instance = this;

		// Initialize server and handler
		Log.Info( "FabBridge: Creating server and import handler" );
		_server = new FabBridgeServer();
		_importHandler = new FabImportHandler();

		// Wire up events
		_server.OnAssetReceived += OnAssetReceived;
		_server.OnStatusChanged += OnServerStatusChanged;
		_server.OnClientConnected += OnClientConnected;
		_server.OnError += OnServerError;

		_importHandler.OnImportStarted += OnImportStarted;
		_importHandler.OnImportCompleted += OnImportCompleted;
		_importHandler.OnProgressUpdate += OnProgressUpdate;

		// Build the UI
		Log.Info( "FabBridge: Building UI layout" );
		BuildLayout();

		// Auto-start server
		Log.Info( $"FabBridge: Auto-starting server on port {_currentPort}" );
		_server.Start( _currentPort );
		Log.Info( "FabBridge: Widget initialization complete" );
	}

	private void BuildLayout()
	{
		WindowTitle = "Fab Bridge";
		MinimumSize = new Vector2( 300, 400 );

		Layout = Layout.Column();
		Layout.Spacing = 8;
		Layout.Margin = 16;

		// Header
		var header = Layout.Add( new Label( "Fab Bridge for s&box" ) );
		header.SetStyles( "font-size: 16px; font-weight: bold;" );

		// Licensing notice
		var notice = Layout.Add( new Label( "Note: Fab assets require a subscription for non-Unreal use." ) );
		notice.SetStyles( "font-size: 11px; color: #888;" );
		notice.WordWrap = true;

		Layout.AddSpacingCell( 8 );

		// Server section
		var serverGroup = new Widget( this );
		serverGroup.Layout = Layout.Column();
		serverGroup.Layout.Spacing = 4;

		var serverLabel = serverGroup.Layout.Add( new Label( "Server Settings" ) );
		serverLabel.SetStyles( "font-weight: bold;" );

		// Port row
		var portRow = new Widget( serverGroup );
		portRow.Layout = Layout.Row();
		portRow.Layout.Spacing = 8;

		portRow.Layout.Add( new Label( "Port:" ) );
		_portEdit = portRow.Layout.Add( new LineEdit( portRow ) );
		_portEdit.Text = FabBridgeServer.DefaultPort.ToString();
		_portEdit.MaximumWidth = 80;
		_portEdit.TextEdited += OnPortChanged;
		portRow.Layout.AddStretchCell();

		serverGroup.Layout.Add( portRow );

		// Button row
		var buttonRow = new Widget( serverGroup );
		buttonRow.Layout = Layout.Row();
		buttonRow.Layout.Spacing = 8;

		_startButton = buttonRow.Layout.Add( new Button( "Start Server", buttonRow ) );
		_startButton.Clicked = OnStartClicked;

		_stopButton = buttonRow.Layout.Add( new Button( "Stop Server", buttonRow ) );
		_stopButton.Clicked = OnStopClicked;
		_stopButton.Enabled = false;

		buttonRow.Layout.AddStretchCell();
		serverGroup.Layout.Add( buttonRow );

		// Status
		var statusRow = new Widget( serverGroup );
		statusRow.Layout = Layout.Row();
		statusRow.Layout.Spacing = 8;

		statusRow.Layout.Add( new Label( "Status:" ) );
		_statusLabel = statusRow.Layout.Add( new Label( "Stopped" ) );
		_statusLabel.SetStyles( "color: #f80;" );
		statusRow.Layout.AddStretchCell();

		serverGroup.Layout.Add( statusRow );
		Layout.Add( serverGroup );

		Layout.AddSpacingCell( 16 );

		// Import settings section
		var importGroup = new Widget( this );
		importGroup.Layout = Layout.Column();
		importGroup.Layout.Spacing = 4;

		var importLabel = importGroup.Layout.Add( new Label( "Import Settings" ) );
		importLabel.SetStyles( "font-weight: bold;" );

		// Import folder row
		var folderRow = new Widget( importGroup );
		folderRow.Layout = Layout.Row();
		folderRow.Layout.Spacing = 8;

		folderRow.Layout.Add( new Label( "Folder:" ) );
		_importFolderEdit = folderRow.Layout.Add( new LineEdit( folderRow ) );
		_importFolderEdit.Text = _importHandler.ImportFolder;
		_importFolderEdit.TextEdited += ( text ) => _importHandler.ImportFolder = text;
		_importFolderEdit.PlaceholderText = "fab_imports";

		importGroup.Layout.Add( folderRow );

		// Checkboxes
		_createMaterialsCheck = importGroup.Layout.Add( new Checkbox( "Create materials automatically" ) );
		_createMaterialsCheck.Value = _importHandler.CreateMaterials;
		_createMaterialsCheck.StateChanged += ( state ) => _importHandler.CreateMaterials = state == CheckState.On;

		_convertModelsCheck = importGroup.Layout.Add( new Checkbox( "Convert models (FBX to VMDL)" ) );
		_convertModelsCheck.Value = _importHandler.ConvertModels;
		_convertModelsCheck.StateChanged += ( state ) => _importHandler.ConvertModels = state == CheckState.On;

		Layout.Add( importGroup );

		Layout.AddSpacingCell( 16 );

		// Import history section
		var historyLabel = Layout.Add( new Label( "Import History" ) );
		historyLabel.SetStyles( "font-weight: bold;" );

		_importHistoryList = Layout.Add( new ListView( this ) );
		_importHistoryList.MinimumHeight = 150;
		_importHistoryList.ItemPaint = PaintHistoryItem;
		_importHistoryList.ItemSize = new Vector2( 0, 32 );

		// Clear history button
		var clearButton = Layout.Add( new Button( "Clear History", this ) );
		clearButton.Clicked = () =>
		{
			_importHistory.Clear();
			UpdateHistoryList();
		};

		Layout.AddStretchCell();

		// Instructions
		var instructions = Layout.Add( new Label( "Configure Fab to use Custom (socket port) export with port " + FabBridgeServer.DefaultPort ) );
		instructions.SetStyles( "font-size: 11px; color: #666;" );
		instructions.WordWrap = true;
	}

	private void OnPortChanged( string text )
	{
		if ( int.TryParse( text, out var port ) && port >= 1024 && port <= 65535 )
		{
			_currentPort = port;
		}
	}

	private void OnStartClicked()
	{
		if ( _server.Start( _currentPort ) )
		{
			_startButton.Enabled = false;
			_stopButton.Enabled = true;
			_portEdit.ReadOnly = true;
		}
	}

	private void OnStopClicked()
	{
		_server.Stop();
		_startButton.Enabled = true;
		_stopButton.Enabled = false;
		_portEdit.ReadOnly = false;
	}

	private void OnServerStatusChanged( string status )
	{
		_statusLabel.Text = status;

		if ( _server.IsRunning )
		{
			_statusLabel.SetStyles( "color: #0f0;" );
			_startButton.Enabled = false;
			_stopButton.Enabled = true;
		}
		else
		{
			_statusLabel.SetStyles( "color: #f80;" );
			_startButton.Enabled = true;
			_stopButton.Enabled = false;
		}
	}

	private void OnClientConnected()
	{
		_statusLabel.Text = "Client connected";
		_statusLabel.SetStyles( "color: #0ff;" );
	}

	private void OnServerError( string error )
	{
		_statusLabel.Text = $"Error: {error}";
		_statusLabel.SetStyles( "color: #f00;" );
	}

	private async void OnAssetReceived( FabExportData exportData )
	{
		Log.Info( $"FabBridge: Received {exportData.Assets.Count} asset(s) for import" );

		// Import all assets
		var results = await _importHandler.ImportAllAsync( exportData );

		foreach ( var result in results )
		{
			_importHistory.Insert( 0, result );
		}

		// Keep history limited
		while ( _importHistory.Count > 50 )
		{
			_importHistory.RemoveAt( _importHistory.Count - 1 );
		}

		UpdateHistoryList();

		// Refresh asset browser
		MainAssetBrowser.Instance?.Local.UpdateAssetList();
	}

	private void OnImportStarted( FabAsset asset )
	{
		_statusLabel.Text = $"Importing: {asset.Name ?? asset.Id}";
		_statusLabel.SetStyles( "color: #ff0;" );
	}

	private void OnImportCompleted( FabImportHandler.ImportResult result )
	{
		if ( result.Success )
		{
			_statusLabel.Text = $"Imported: {result.AssetName}";
			_statusLabel.SetStyles( "color: #0f0;" );
		}
		else
		{
			_statusLabel.Text = $"Failed: {result.AssetName}";
			_statusLabel.SetStyles( "color: #f00;" );
		}
	}

	private void OnProgressUpdate( string message )
	{
		_statusLabel.Text = message;
	}

	private void UpdateHistoryList()
	{
		_importHistoryList.SetItems( _importHistory );
	}

	private void PaintHistoryItem( VirtualWidget item )
	{
		if ( item.Object is not FabImportHandler.ImportResult result )
			return;

		var rect = item.Rect;

		// Background for selected
		if ( item.Selected )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( rect );
		}

		// Status icon
		var iconRect = rect.Shrink( 4 );
		iconRect.Width = 16;

		Paint.SetPen( result.Success ? Theme.Green : Theme.Red );
		Paint.DrawIcon( iconRect, result.Success ? "check_circle" : "error", 14 );

		// Asset name
		var textRect = rect.Shrink( 4 );
		textRect.Left += 24;

		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont();
		Paint.DrawText( textRect, result.AssetName, TextFlag.LeftCenter );

		// Time
		var timeRect = rect.Shrink( 4 );
		timeRect.Left = timeRect.Right - 80;

		Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
		Paint.SetDefaultFont( 9 );
		Paint.DrawText( timeRect, result.ImportTime.ToString( "HH:mm:ss" ), TextFlag.RightCenter );
	}

	public override void OnDestroyed()
	{
		_server?.Dispose();
		base.OnDestroyed();
	}
}
