using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodeLink;
internal static class MacSecret
{
    private static readonly Lazy<byte[]> key=new(LoadKey);
    public static string Protect(string text)
    {
        var plain=Encoding.UTF8.GetBytes(text);var nonce=RandomNumberGenerator.GetBytes(12);var tag=new byte[16];var cipher=new byte[plain.Length];
        using var aes=new AesGcm(key.Value,16);aes.Encrypt(nonce,plain,cipher,tag);
        return "keychain:"+Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public static string Unprotect(string text)
    {
        var bytes=Convert.FromBase64String(text[9..]);if(bytes.Length<28)throw new CryptographicException("Invalid saved secret");
        var plain=new byte[bytes.Length-28];using var aes=new AesGcm(key.Value,16);aes.Decrypt(bytes.AsSpan(0,12),bytes.AsSpan(28),bytes.AsSpan(12,16),plain);
        return Encoding.UTF8.GetString(plain);
    }
    private static byte[] LoadKey()
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var service=Encoding.UTF8.GetBytes("com.codelink.host");var account=Encoding.UTF8.GetBytes("encryption-key-v1");
        var result=SecKeychainFindGenericPassword(IntPtr.Zero,(uint)service.Length,service,(uint)account.Length,account,out var length,out var data,IntPtr.Zero);
        if(result==0)
        {
            try{if(length!=32)throw new CryptographicException("Invalid Code Link Keychain item");var bytes=new byte[length];Marshal.Copy(data,bytes,0,bytes.Length);return bytes;}
            finally{SecKeychainItemFreeContent(IntPtr.Zero,data);}
        }
        if(result!=-25300)throw new CryptographicException("Unlock your login Keychain. OSStatus "+result);
        var created=RandomNumberGenerator.GetBytes(32);
        result=SecKeychainAddGenericPassword(IntPtr.Zero,(uint)service.Length,service,(uint)account.Length,account,(uint)created.Length,created,IntPtr.Zero);
        if(result!=0)throw new CryptographicException("Cannot save Code Link key in login Keychain. OSStatus "+result);
        return created;
    }
    private const string Security="/System/Library/Frameworks/Security.framework/Security";
    [DllImport(Security)]private static extern int SecKeychainFindGenericPassword(IntPtr keychain,uint serviceLength,byte[] service,uint accountLength,byte[] account,out uint passwordLength,out IntPtr password,IntPtr item);
    [DllImport(Security)]private static extern int SecKeychainAddGenericPassword(IntPtr keychain,uint serviceLength,byte[] service,uint accountLength,byte[] account,uint passwordLength,byte[] password,IntPtr item);
    [DllImport(Security)]private static extern int SecKeychainItemFreeContent(IntPtr attributes,IntPtr data);
}
