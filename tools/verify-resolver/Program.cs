// 验证就地 IL 补丁产物的结构完整性（dnlib 加载 + 指令流检查）。
// 用法: dotnet run --project tools/verify-resolver -- <resolver.dll>
//
// 判定标准：
// 1. dnlib 能完整加载（元数据/IL 结构合法）；
// 2. 全程序集不再有 ldstr "The digital signature is invalid."（错误路径被消除）；
// 3. ValidateSignature 各方法体内存在补丁形态：Pop 后紧跟 ≥3 个 Nop
//    （就地形态 pop+12×nop 与 dnlib 重写形态 pop+3×nop 都满足）。
using dnlib.DotNet;
using dnlib.DotNet.Emit;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: verify-resolver <resolver.dll>");
    return 1;
}

var path = args[0];
var dump = args.Contains("--dump");
ModuleDefMD module;
try
{
    module = ModuleDefMD.Load(path);
    _ = module.GetTypes().Count(); // 触发全量元数据读取，让损坏在加载期暴露
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: cannot load as managed assembly: {ex.Message}");
    return 2;
}

if (dump)
{
    foreach (var type in module.GetTypes())
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody || method.Name != "ValidateSignature")
                continue;
            Console.WriteLine($"===== {type.FullName}.{method.Name} =====");
            foreach (var instr in method.Body.Instructions)
            {
                var operand = instr.Operand switch
                {
                    string s => $"\"{s}\"",
                    null => "",
                    var o => o.ToString(),
                };
                Console.WriteLine($"  {instr.Offset:X4}: {instr.OpCode} {operand}".TrimEnd());
            }
        }
    }
}

var errorStringLdstr = 0;
var vsMethods = 0;
var vsPatchedMethods = 0;
foreach (var type in module.GetTypes())
{
    foreach (var method in type.Methods)
    {
        if (!method.HasBody)
            continue;

        var instrs = method.Body.Instructions;
        if (method.Name == "ValidateSignature")
        {
            vsMethods++;
            // 补丁形态：Pop 之后 ≥3 个 Nop（指令流层面，须在方法体内）
            var patched = false;
            for (int i = 0; i < instrs.Count - 3; i++)
            {
                if (instrs[i].OpCode == OpCodes.Pop
                    && instrs[i + 1].OpCode == OpCodes.Nop
                    && instrs[i + 2].OpCode == OpCodes.Nop
                    && instrs[i + 3].OpCode == OpCodes.Nop)
                {
                    patched = true;
                    break;
                }
            }
            if (patched)
            {
                vsPatchedMethods++;
                Console.WriteLine($"  patched: {type.FullName}.{method.Name} ({instrs.Count} instrs)");
            }
        }

        foreach (var instr in instrs)
        {
            if (instr.OpCode == OpCodes.Ldstr
                && instr.Operand is string s
                && s == "The digital signature is invalid.")
            {
                errorStringLdstr++;
                Console.WriteLine($"  still throws: {type.FullName}.{method.Name} (ldstr @ {instr.Offset:X4})");
            }
        }
    }
}

Console.WriteLine($"assembly identity: {module.Assembly?.FullName}");
Console.WriteLine($"ValidateSignature methods: {vsMethods}, patched (Pop+3×Nop): {vsPatchedMethods}");
Console.WriteLine($"remaining ldstr of error string: {errorStringLdstr}");
// XmlSchemaValidator.ValidateSignature 是另一类校验（不抛该错误），无需补丁；
// 判定以"含错误路径的 ValidateSignature 已打补丁 + 错误字符串零残留"为准。
var ok = vsMethods > 0 && vsPatchedMethods >= 1 && errorStringLdstr == 0;
Console.WriteLine(ok ? "OK: resolver is patched and structurally valid" : "WARN: unexpected state");
return ok ? 0 : 3;
