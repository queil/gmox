namespace Queil.Gmox

module Emit =
  open System.Reflection
  open System.Reflection.Emit
  open System
  open Types

  let makeImpl (typ:Type) =

    if not <| typ.IsAbstract then
      failwithf "Expecting a service base type which should be abstract. Given type: '%s'" (typ.AssemblyQualifiedName)

    let uid () = Guid.NewGuid().ToString("N").Remove(10)
    let asmName = AssemblyName($"grpc-dynamic-{uid ()}")
    let asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run)
    let moduleBuilder = asmBuilder.DefineDynamicModule("grpc-impl")
    let typeBuilder = moduleBuilder.DefineType($"{typ.Name}-{uid ()}")
    typeBuilder.SetParent(typ)

    let storeFld = typeBuilder.DefineField("_store", typeof<StubStore>, FieldAttributes.Public)

    let ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [|typeof<StubStore>|])
    let ctorIl = ctorBuilder.GetILGenerator()
    ctorIl.Emit(OpCodes.Ldarg_0)
    ctorIl.Emit(OpCodes.Ldarg_1)
    ctorIl.Emit(OpCodes.Stfld, storeFld)
    ctorIl.Emit(OpCodes.Ret)

    for method in typ.GetMethods(BindingFlags.Public ||| BindingFlags.DeclaredOnly ||| BindingFlags.Instance) do
      let methodBuilder =
        typeBuilder.DefineMethod(
          $"{typ.Name}.{method.Name}",
          MethodAttributes.Public ||| MethodAttributes.Virtual,
          CallingConventions.HasThis,
          method.ReturnType,
          method.GetParameters() |> Array.map (fun p -> p.ParameterType))

      let findStub = storeFld.FieldType.GetMethod(StubStore.FindStubMethodName)
      let getCurrentMethod = typeof<MethodBase>.GetMethod(nameof(MethodBase.GetCurrentMethod), BindingFlags.Public ||| BindingFlags.Static)

      let il = methodBuilder.GetILGenerator()
       
      il.Emit(OpCodes.Ldarg_0)                 // push Grpc svc on to the stack (this)
      il.Emit(OpCodes.Ldfld, storeFld)         // push Store instance reference on to the stack
      il.Emit(OpCodes.Ldarg_1)                 // push grpc method request reference
      il.Emit(OpCodes.Ldarg_2)                 // push grpc method server call context

      il.Emit(OpCodes.Call, getCurrentMethod)  // push currently executing grpc method info on
      il.Emit(OpCodes.Callvirt, findStub)      // now call findStub
      il.Emit(OpCodes.Ret)                     // return the retrieved response

      typeBuilder.DefineMethodOverride(methodBuilder, method)
    let t = typeBuilder.CreateType()
    t
