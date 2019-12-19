#if SECURITY_DEP
#if MONO_SECURITY_ALIAS
extern alias MonoSecurity;
#endif

using System;
using System.Text;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using MNS = Mono.Net.Security;
#if MONO_SECURITY_ALIAS
using MonoSecurity::Mono.Security.Interface;
#else
using Mono.Security.Interface;
#endif

using size_t = System.IntPtr;

namespace Mono.Unity
{
	unsafe internal class UnityTlsProvider : MonoTlsProvider
	{
		public override string Name {
			get { return "unitytls"; }
		}

		public override Guid ID => MNS.MonoTlsProviderFactory.UnityTlsId;
		public override bool SupportsSslStream => true;
		public override bool SupportsMonoExtensions => true;
		public override bool SupportsConnectionInfo => true;
		internal override bool SupportsCleanShutdown => true;
		public override SslProtocols SupportedProtocols => SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;

		public override IMonoSslStream CreateSslStream (
			Stream innerStream, bool leaveInnerStreamOpen,
			MonoTlsSettings settings = null)
		{
			return SslStream.CreateMonoSslStream (innerStream, leaveInnerStreamOpen, this, settings);
		}

		internal override IMonoSslStream CreateSslStreamInternal (
			SslStream sslStream, Stream innerStream, bool leaveInnerStreamOpen,
			MonoTlsSettings settings)
		{
			return new UnityTlsStream (innerStream, leaveInnerStreamOpen, sslStream, settings, this);
		}

		internal override bool ValidateCertificate (
			ICertificateValidator2 validator, string targetHost, bool serverMode,
			X509CertificateCollection certificates, bool wantsChain, ref X509Chain chain,
			ref MonoSslPolicyErrors errors, ref int status11)
		{
			bool hasUnityTlsChain = (chain != null && chain.Impl is X509ChainImplUnityTls);

			if (certificates == null && !hasUnityTlsChain) {
				errors |= MonoSslPolicyErrors.RemoteCertificateNotAvailable;
				return false;
			}

			if (wantsChain && !hasUnityTlsChain)
				chain = MNS.SystemCertificateValidator.CreateX509Chain (certificates);

			if (certificates == null || certificates.Count == 0) {
				errors |= MonoSslPolicyErrors.RemoteCertificateNotAvailable;
				return false;
			}

			// fixup targetHost name by removing port
			if (!string.IsNullOrEmpty (targetHost)) {
				var pos = targetHost.IndexOf (':');
				if (pos > 0)
					targetHost = targetHost.Substring (0, pos);
			}

			// convert cert to native
			var errorState = UnityTls.NativeInterface.unitytls_errorstate_create ();
			var result = UnityTls.unitytls_x509verify_result.UNITYTLS_X509VERIFY_NOT_DONE;
			UnityTls.unitytls_x509list* certificatesNative = null;
			try
			{
				// Things the validator provides that we might want to make use of here:
				//validator.Settings.CheckCertificateName				// not used by mono?
				//validator.Settings.CheckCertificateRevocationStatus	// not used by mono?
				//validator.Settings.CertificateValidationTime
				//validator.Settings.CertificateSearchPaths				// currently only used by MonoBtlsProvider

				UnityTls.unitytls_x509list_ref certificatesNativeRef;
				if (!hasUnityTlsChain)
				{
					certificatesNative = UnityTls.NativeInterface.unitytls_x509list_create (&errorState);
					CertHelper.AddCertificatesToNativeChain (certificatesNative, certificates, &errorState);
					certificatesNativeRef = UnityTls.NativeInterface.unitytls_x509list_get_ref (certificatesNative, &errorState);
				}
				else
					certificatesNativeRef = ((X509ChainImplUnityTls)chain.Impl).NativeCertificateChain;
				
				var targetHostUtf8 = Encoding.UTF8.GetBytes (targetHost);

				if (validator.Settings.TrustAnchors != null) {
					UnityTls.unitytls_x509list* trustCAnative = null;
					try
					{
						trustCAnative = UnityTls.NativeInterface.unitytls_x509list_create (&errorState);
						CertHelper.AddCertificatesToNativeChain (trustCAnative, validator.Settings.TrustAnchors, &errorState);
						var trustCAnativeRef = UnityTls.NativeInterface.unitytls_x509list_get_ref (trustCAnative, &errorState);

						fixed (byte* targetHostUtf8Ptr = targetHostUtf8) {
							result = UnityTls.NativeInterface.unitytls_x509verify_explicit_ca (certificatesNativeRef, trustCAnativeRef, targetHostUtf8Ptr, (size_t)targetHostUtf8.Length, null, null, &errorState);
						}
					}
					finally {
						UnityTls.NativeInterface.unitytls_x509list_free (trustCAnative);
					}
				} else {
					fixed (byte* targetHostUtf8Ptr = targetHostUtf8) {
						result = UnityTls.NativeInterface.unitytls_x509verify_default_ca (certificatesNativeRef, targetHostUtf8Ptr, (size_t)targetHostUtf8.Length, null, null, &errorState);
					}
				}
			}
			finally	{
				UnityTls.NativeInterface.unitytls_x509list_free (certificatesNative);
			}

			errors = UnityTlsConversions.VerifyResultToPolicyErrror(result);
			return result == UnityTls.unitytls_x509verify_result.UNITYTLS_X509VERIFY_SUCCESS && 
					errorState.code == UnityTls.unitytls_error_code.UNITYTLS_SUCCESS;
		}
	}
}
#endif
