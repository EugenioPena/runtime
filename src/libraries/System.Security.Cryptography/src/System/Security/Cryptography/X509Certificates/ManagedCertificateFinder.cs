// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography.X509Certificates.Asn1;
using Internal.Cryptography;

namespace System.Security.Cryptography.X509Certificates
{
    internal abstract class ManagedCertificateFinder : IFindPal
    {
        private readonly X509Certificate2Collection _findFrom;
        private readonly X509Certificate2Collection _copyTo;
        private readonly bool _validOnly;

        internal ManagedCertificateFinder(X509Certificate2Collection findFrom, X509Certificate2Collection copyTo, bool validOnly)
        {
            _findFrom = findFrom;
            _copyTo = copyTo;
            _validOnly = validOnly;
        }

        public string NormalizeOid(string maybeOid, OidGroup expectedGroup)
        {
            Oid oid = new Oid(maybeOid);

            // If maybeOid is interpreted to be a FriendlyName, return the OID.
            if (!StringComparer.OrdinalIgnoreCase.Equals(oid.Value, maybeOid))
            {
                return oid.Value!;
            }

            FindPal.ValidateOidValue(maybeOid);
            return maybeOid;
        }

        public void FindByThumbprint(byte[] thumbprint)
        {
            static bool FindPredicate(byte[] thumbprint, X509Certificate2 certificate)
            {
                // Bail up front if the length is incorrect to avoid any hash work.
                if (thumbprint.Length != SHA1.HashSizeInBytes)
                {
                    return false;
                }

                Span<byte> hashBuffer = stackalloc byte[SHA1.HashSizeInBytes];

                if (!certificate.TryGetCertHash(HashAlgorithmName.SHA1, hashBuffer, out int hashBytesWritten) ||
                    hashBytesWritten != SHA1.HashSizeInBytes)
                {
                    Debug.Fail("Presized hash buffer was not the correct size.");
                    throw new CryptographicException();
                }

                return hashBuffer.SequenceEqual(thumbprint);
            }

            FindCore(thumbprint, FindPredicate);
        }

        public void FindBySubjectName(string subjectName)
        {
            FindCore(
                subjectName,
                static (subjectName, cert) =>
                {
                    string formedSubject = X500NameEncoder.X500DistinguishedNameDecode(cert.SubjectName.RawData, false, X500DistinguishedNameFlags.None);

                    return formedSubject.Contains(subjectName, StringComparison.OrdinalIgnoreCase);
                });
        }

        public void FindBySubjectDistinguishedName(string subjectDistinguishedName)
        {
            FindCore(subjectDistinguishedName, static (subjectDistinguishedName, cert) => StringComparer.OrdinalIgnoreCase.Equals(subjectDistinguishedName, cert.Subject));
        }

        public void FindByIssuerName(string issuerName)
        {
            FindCore(
                issuerName,
                static (issuerName, cert) =>
                {
                    string formedIssuer = X500NameEncoder.X500DistinguishedNameDecode(cert.IssuerName.RawData, false, X500DistinguishedNameFlags.None);

                    return formedIssuer.Contains(issuerName, StringComparison.OrdinalIgnoreCase);
                });
        }

        public void FindByIssuerDistinguishedName(string issuerDistinguishedName)
        {
            FindCore(issuerDistinguishedName, static (issuerDistinguishedName, cert) => StringComparer.OrdinalIgnoreCase.Equals(issuerDistinguishedName, cert.Issuer));
        }

        public void FindBySerialNumber(BigInteger hexValue, BigInteger decimalValue)
        {
            FindCore(
                (hexValue, decimalValue),
                static (state, cert) =>
                {
                    byte[] serialBytes = cert.GetSerialNumber();
                    BigInteger serialNumber = new BigInteger(serialBytes, isUnsigned: true);
                    bool match = state.hexValue.Equals(serialNumber) || state.decimalValue.Equals(serialNumber);

                    return match;
                });
        }

        private static DateTime NormalizeDateTime(DateTime dateTime)
        {
            // If it's explicitly UTC, convert it to local before a comparison, since the
            // NotBefore and NotAfter are in Local time.
            //
            // If it was Unknown, assume it was Local.
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                return dateTime.ToLocalTime();
            }

            return dateTime;
        }

        public void FindByTimeValid(DateTime dateTime)
        {
            DateTime normalized = NormalizeDateTime(dateTime);

            FindCore(normalized, static (normalized, cert) => cert.NotBefore <= normalized && normalized <= cert.NotAfter);
        }

        public void FindByTimeNotYetValid(DateTime dateTime)
        {
            DateTime normalized = NormalizeDateTime(dateTime);

            FindCore(normalized, static (normalized, cert) => cert.NotBefore > normalized);
        }

        public void FindByTimeExpired(DateTime dateTime)
        {
            DateTime normalized = NormalizeDateTime(dateTime);

            FindCore(normalized, static (normalized, cert) => cert.NotAfter < normalized);
        }

        public void FindByTemplateName(string templateName)
        {
            FindCore(
                templateName,
                static (templateName, cert) =>
                {
                    X509Extension? ext = FindExtension(cert, Oids.EnrollCertTypeExtension);

                    if (ext != null)
                    {
                        string decodedName;

                        try
                        {
                            // Try a V1 template structure, just a string:
                            AsnReader reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
                            decodedName = reader.ReadAnyAsnString();
                            reader.ThrowIfNotEmpty();
                        }
                        catch (AsnContentException e)
                        {
                            throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding, e);
                        }

                        // If this doesn't match, maybe a V2 template will
                        if (StringComparer.OrdinalIgnoreCase.Equals(templateName, decodedName))
                        {
                            return true;
                        }
                    }

                    ext = FindExtension(cert, Oids.CertificateTemplate);

                    if (ext != null)
                    {
                        CertificateTemplateAsn template = CertificateTemplateAsn.Decode(ext.RawData, AsnEncodingRules.DER);
                        if (StringComparer.Ordinal.Equals(templateName, template.TemplateID))
                        {
                            return true;
                        }
                    }

                    return false;
                });
        }

        public void FindByApplicationPolicy(string oidValue)
        {
            FindCore(
                oidValue,
                static (oidValue, cert) =>
                {
                    X509Extension? ext = FindExtension(cert, Oids.EnhancedKeyUsage);

                    if (ext == null)
                    {
                        // A certificate with no EKU is valid for all extended purposes.
                        return true;
                    }

                    var ekuExt = (X509EnhancedKeyUsageExtension)ext;

                    foreach (Oid usageOid in ekuExt.EnhancedKeyUsages)
                    {
                        if (StringComparer.Ordinal.Equals(oidValue, usageOid.Value))
                        {
                            return true;
                        }
                    }

                    // If the certificate had an EKU extension, and the value we wanted was
                    // not present, then it is not valid for that usage.
                    return false;
                });
        }

        public void FindByCertificatePolicy(string oidValue)
        {
            FindCore(
                oidValue,
                static (oidValue, cert) =>
                {
                    X509Extension? ext = FindExtension(cert, Oids.CertPolicies);

                    if (ext == null)
                    {
                        // Unlike Application Policy, Certificate Policy is "assume false".
                        return false;
                    }

                    ISet<string> policyOids = CertificatePolicyChain.ReadCertPolicyExtension(ext.RawData);
                    return policyOids.Contains(oidValue);
                });
        }

        public void FindByExtension(string oidValue)
        {
            FindCore(oidValue, static (oidValue, cert) => FindExtension(cert, oidValue) != null);
        }

        public void FindByKeyUsage(X509KeyUsageFlags keyUsage)
        {
            FindCore(
                keyUsage,
                static (keyUsage, cert) =>
                {
                    X509Extension? ext = FindExtension(cert, Oids.KeyUsage);

                    if (ext == null)
                    {
                        // A certificate with no key usage extension is considered valid for all key usages.
                        return true;
                    }

                    var kuExt = (X509KeyUsageExtension)ext;

                    return (kuExt.KeyUsages & keyUsage) == keyUsage;
                });
        }

        protected abstract byte[] GetSubjectPublicKeyInfo(X509Certificate2 cert);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5350", Justification = "SHA1 is required for Compat")]
        public void FindBySubjectKeyIdentifier(byte[] keyIdentifier)
        {
            FindCore(
                keyIdentifier,
                (keyIdentifier, cert) =>
                {
                    X509Extension? ext = FindExtension(cert, Oids.SubjectKeyIdentifier);
                    Span<byte> certKeyId = stackalloc byte[0];

                    if (ext != null)
                    {
                        // The extension exposes the value as a hexadecimal string, or we can decode here.
                        // Enough parsing has gone on, let's decode.
                        certKeyId = ManagedX509ExtensionProcessor.DecodeX509SubjectKeyIdentifierExtension(ext.RawData);
                    }
                    else
                    {
                        // The Desktop/Windows version of this method use CertGetCertificateContextProperty
                        // with a property ID of CERT_KEY_IDENTIFIER_PROP_ID.
                        //
                        // MSDN says that when there's no extension, this method takes the SHA-1 of the
                        // SubjectPublicKeyInfo block, and returns that.
                        //
                        // https://msdn.microsoft.com/en-us/library/windows/desktop/aa376079%28v=vs.85%29.aspx
                        certKeyId = stackalloc byte[SHA1.HashSizeInBytes];
                        byte[] publicKeyInfoBytes = GetSubjectPublicKeyInfo(cert);
                        int written = SHA1.HashData(publicKeyInfoBytes, certKeyId);
                        Debug.Assert(written == SHA1.HashSizeInBytes);
                    }

                    return certKeyId.SequenceEqual(keyIdentifier);
                });
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        private static X509Extension? FindExtension(X509Certificate2 cert, string extensionOid)
        {
            if (cert.Extensions == null || cert.Extensions.Count == 0)
            {
                return null;
            }

            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext != null &&
                    ext.Oid != null &&
                    StringComparer.Ordinal.Equals(extensionOid, ext.Oid.Value))
                {
                    return ext;
                }
            }

            return null;
        }

        protected abstract X509Certificate2 CloneCertificate(X509Certificate2 cert);

        private void FindCore<TState>(TState state, Func<TState, X509Certificate2, bool> predicate)
        {
            X509Certificate2Collection findFrom = _findFrom;
            int count = findFrom.Count;
            for (int i = 0; i < count; i++)
            {
                X509Certificate2 cert = findFrom[i];
                if (predicate(state, cert))
                {
                    if (!_validOnly || IsCertValid(cert))
                    {
                        X509Certificate2 clone = CloneCertificate(cert);
                        Debug.Assert(!ReferenceEquals(cert, clone));

                        _copyTo.Add(clone);
                    }
                }
            }
        }

        private static bool IsCertValid(X509Certificate2 cert)
        {
            try
            {
                // This needs to be kept in sync with VerifyCertificateIgnoringErrors in the
                // Windows PAL version (and potentially any other PALs that come about)
                using (X509Chain chain = new X509Chain
                {
                    ChainPolicy =
                    {
                        RevocationMode = X509RevocationMode.NoCheck,
                        RevocationFlag = X509RevocationFlag.ExcludeRoot
                    }
                })
                {
                    bool valid = chain.Build(cert);
                    int elementCount = chain.ChainElements.Count;

                    for (int i = 0; i < elementCount; i++)
                    {
                        chain.ChainElements[i].Certificate.Dispose();
                    }

                    return valid;
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
