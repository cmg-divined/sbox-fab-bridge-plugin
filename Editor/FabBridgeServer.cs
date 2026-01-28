using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FabBridge;

/// <summary>
/// TCP socket server that listens for Fab/Quixel Bridge exports
/// </summary>
public class FabBridgeServer : IDisposable
{
	public const int DefaultPort = 24981;

	private TcpListener _listener;
	private CancellationTokenSource _cts;
	private Task _listenTask;
	private bool _isRunning;

	/// <summary>
	/// The port the server is listening on
	/// </summary>
	public int Port { get; private set; }

	/// <summary>
	/// Whether the server is currently running
	/// </summary>
	public bool IsRunning => _isRunning;

	/// <summary>
	/// Event raised when an asset export is received from Fab
	/// </summary>
	public event Action<FabExportData> OnAssetReceived;

	/// <summary>
	/// Event raised when a connection is established
	/// </summary>
	public event Action OnClientConnected;

	/// <summary>
	/// Event raised when the server status changes
	/// </summary>
	public event Action<string> OnStatusChanged;

	/// <summary>
	/// Event raised when an error occurs
	/// </summary>
	public event Action<string> OnError;

	/// <summary>
	/// Start the server on the specified port
	/// </summary>
	public bool Start( int port = DefaultPort )
	{
		if ( _isRunning )
		{
			Log.Warning( "FabBridge server is already running" );
			return false;
		}

		try
		{
			Port = port;
			_cts = new CancellationTokenSource();
			_listener = new TcpListener( IPAddress.Loopback, port );
			_listener.Start();
			_isRunning = true;

			_listenTask = Task.Run( ListenLoop, _cts.Token );

			Log.Info( $"FabBridge server started on port {port}" );
			OnStatusChanged?.Invoke( $"Listening on port {port}" );
			return true;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to start FabBridge server: {ex.Message}" );
			OnError?.Invoke( $"Failed to start: {ex.Message}" );
			_isRunning = false;
			return false;
		}
	}

	/// <summary>
	/// Stop the server
	/// </summary>
	public void Stop()
	{
		if ( !_isRunning )
			return;

		try
		{
			_cts?.Cancel();
			_listener?.Stop();
			_isRunning = false;

			Log.Info( "FabBridge server stopped" );
			OnStatusChanged?.Invoke( "Stopped" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Error stopping FabBridge server: {ex.Message}" );
		}
	}

	private async Task ListenLoop()
	{
		while ( !_cts.Token.IsCancellationRequested )
		{
			try
			{
				// Use Task.Run with cancellation to make AcceptTcpClientAsync cancellable
				var acceptTask = _listener.AcceptTcpClientAsync();
				var completedTask = await Task.WhenAny( acceptTask, Task.Delay( -1, _cts.Token ) );

				if ( completedTask != acceptTask )
				{
					// Cancellation was requested
					break;
				}

				var client = await acceptTask;
				Log.Info( "FabBridge: Client connected" );
				OnClientConnected?.Invoke();

				// Handle client in background
				_ = Task.Run( () => HandleClient( client ) );
			}
			catch ( OperationCanceledException )
			{
				break;
			}
			catch ( ObjectDisposedException )
			{
				// Listener was stopped
				break;
			}
			catch ( Exception ex )
			{
				if ( _isRunning )
				{
					Log.Warning( $"FabBridge listen error: {ex.Message}" );
				}
			}
		}
	}

	private async Task HandleClient( TcpClient client )
	{
		try
		{
			using ( client )
			using ( var stream = client.GetStream() )
			{
				// Set a read timeout to prevent hanging connections
				client.ReceiveTimeout = 5000; // 5 second timeout
				
				var buffer = new byte[1024 * 1024]; // 1MB buffer for large JSON
				var messageBuilder = new StringBuilder();
				var idleCount = 0;
				const int maxIdleIterations = 500; // 5 seconds of idle = disconnect

				while ( client.Connected && !_cts.Token.IsCancellationRequested )
				{
					if ( stream.DataAvailable )
					{
						idleCount = 0; // Reset idle counter
						var bytesRead = await stream.ReadAsync( buffer, 0, buffer.Length, _cts.Token );
						if ( bytesRead == 0 )
							break;

						var data = Encoding.UTF8.GetString( buffer, 0, bytesRead );
						messageBuilder.Append( data );

						// Try to parse complete JSON messages
						var message = messageBuilder.ToString();
						if ( TryParseCompleteJson( message, out var json, out var remaining ) )
						{
							messageBuilder.Clear();
							messageBuilder.Append( remaining );

							ProcessMessage( json );
							
							// After processing a message, close the connection
							// Fab sends one message per connection
							break;
						}
					}
					else
					{
						idleCount++;
						if ( idleCount >= maxIdleIterations )
						{
							Log.Info( "FabBridge: Client idle timeout, closing connection" );
							break;
						}
						await Task.Delay( 10, _cts.Token );
					}
				}
			}
		}
		catch ( OperationCanceledException )
		{
			// Expected when stopping
		}
		catch ( Exception ex )
		{
			Log.Warning( $"FabBridge client error: {ex.Message}" );
			OnError?.Invoke( $"Client error: {ex.Message}" );
		}
	}

	/// <summary>
	/// Attempts to extract a complete JSON object from the buffer
	/// </summary>
	private bool TryParseCompleteJson( string data, out string json, out string remaining )
	{
		json = null;
		remaining = data;

		// Find the start of JSON
		var start = data.IndexOf( '{' );
		if ( start == -1 )
			return false;

		// Count braces to find complete object
		var braceCount = 0;
		var inString = false;
		var escaped = false;

		for ( int i = start; i < data.Length; i++ )
		{
			var c = data[i];

			if ( escaped )
			{
				escaped = false;
				continue;
			}

			if ( c == '\\' && inString )
			{
				escaped = true;
				continue;
			}

			if ( c == '"' )
			{
				inString = !inString;
				continue;
			}

			if ( inString )
				continue;

			if ( c == '{' )
				braceCount++;
			else if ( c == '}' )
			{
				braceCount--;
				if ( braceCount == 0 )
				{
					json = data.Substring( start, i - start + 1 );
					remaining = i + 1 < data.Length ? data.Substring( i + 1 ) : "";
					return true;
				}
			}
		}

		return false;
	}

	private void ProcessMessage( string json )
	{
		try
		{
			Log.Info( $"FabBridge: Received message ({json.Length} chars)" );

			// Parse the JSON
			var exportData = FabJsonProtocol.Parse( json );
			if ( exportData != null && exportData.Assets.Count > 0 )
			{
				Log.Info( $"FabBridge: Parsed {exportData.Assets.Count} asset(s)" );
				OnStatusChanged?.Invoke( $"Received {exportData.Assets.Count} asset(s)" );

				// Invoke on main thread
				MainThread.Queue( () =>
				{
					OnAssetReceived?.Invoke( exportData );
				} );
			}
			else
			{
				Log.Warning( "FabBridge: No assets found in message" );
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"FabBridge: Failed to parse message: {ex.Message}" );
			OnError?.Invoke( $"Parse error: {ex.Message}" );
		}
	}

	public void Dispose()
	{
		Stop();
		_cts?.Dispose();
	}
}
