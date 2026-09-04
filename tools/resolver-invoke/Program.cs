// 直接反射调用 XmlExtensions.ValidateSignature，验证补丁语义：
// - 原版：抛出 InvalidDataException("The digital signature is invalid.")（控制组）
// - 补丁后：不再抛该异常（可返回或抛其他类型异常）
// 用法: dotnet run --project tools/resolver-invoke -- <resolver.dll> <xml-doc-string>
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: resolver-invoke <resolver.dll> [xml]");
    return 1;
}

var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var type = assembly.GetType("Unity.Licensing.EntitlementResolver.Xml.XmlExtensions")
    ?? throw new InvalidOperationException("type XmlExtensions not found");
var method = type.GetMethod(
    "ValidateSignature",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
) ?? throw new InvalidOperationException("ValidateSignature not found");

var xml = args.Length > 1
    ? args[1]
    : "<root><License id=\"Terms\"><Signature><SignedInfo><SignatureValue>AA==</SignatureValue></SignedInfo></Signature></License></root>";

try
{
    var doc = new XmlDocument();
    doc.LoadXml(xml);
    var result = method.Invoke(null, new object?[] { doc, null, false, null });
    Console.WriteLine($"RETURNED (no exception) -> patch semantics OK, result: {result}");
    return 0;
}
catch (TargetInvocationException tie)
{
    var ex = tie.InnerException!;
    Console.WriteLine($"THREW {ex.GetType().Name}: {ex.Message}");
    return ex is InvalidDataException ? 10 : 20;
}
catch (Exception ex)
{
    Console.WriteLine($"SETUP FAILED: {ex}");
    return 30;
}
