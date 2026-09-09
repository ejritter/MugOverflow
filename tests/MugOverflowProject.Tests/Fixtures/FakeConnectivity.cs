using Microsoft.Maui.Networking;

namespace MugOverflowProject.Tests.Fixtures;

/// <summary>
/// Controllable mock connectivity service implementing Microsoft.Maui.Networking.IConnectivity
/// for unit testing offline states, reconnection flushes, and network resilience.
/// </summary>
public sealed class FakeConnectivity : IConnectivity
{
	private NetworkAccess _networkAccess = NetworkAccess.Internet;
	private readonly List<ConnectionProfile> _profiles = [ConnectionProfile.WiFi];

	public NetworkAccess NetworkAccess
	{
		get => _networkAccess;
		set
		{
			if (_networkAccess != value)
			{
				_networkAccess = value;
				ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(_networkAccess, _profiles));
			}
		}
	}

	public IEnumerable<ConnectionProfile> ConnectionProfiles => _profiles;

	public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

	public void SetNetworkAccess(NetworkAccess access)
	{
		NetworkAccess = access;
	}

	public void TriggerConnectivityChanged(NetworkAccess access, IEnumerable<ConnectionProfile>? profiles = null)
	{
		_networkAccess = access;
		ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(access, profiles ?? _profiles));
	}
}
