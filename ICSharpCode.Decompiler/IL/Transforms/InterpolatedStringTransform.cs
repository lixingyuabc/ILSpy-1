// Copyright (c) 2021 Siegfried Pammer
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	public class InterpolatedStringTransform : IStatementTransform
	{
		void IStatementTransform.Run(Block block, int pos, StatementTransformContext context)
		{
			if (!context.Settings.StringInterpolation)
				return;
			int interpolationStart = pos;
			int interpolationEnd;
			Call insertionPoint;
			// stloc v(newobj DefaultInterpolatedStringHandler..ctor(ldc.i4 literalLength, ldc.i4 formattedCount))
			if (block.Instructions[pos] is StLoc
				{
					Variable: ILVariable { Kind: VariableKind.Local } v,
					Value: NewObj { Arguments: { Count: 2 } } newObj
				} stloc
				&& v.Type.IsKnownType(KnownTypeCode.DefaultInterpolatedStringHandler)
				&& newObj.Method.DeclaringType.IsKnownType(KnownTypeCode.DefaultInterpolatedStringHandler)
				&& newObj.Arguments[0].MatchLdcI4(out int literalLength)
				&& newObj.Arguments[1].MatchLdcI4(out int formattedCount))
			{
				// { call MethodName(ldloca v, ...) }
				do
				{
					pos++;
				}
				while (IsKnownCall(block, pos, v));
				interpolationEnd = pos;
				// ... call ToStringAndClear(ldloca v) ...
				if (!FindToStringAndClear(block, pos, v, out insertionPoint))
				{
					return;
				}
				if (!(v.StoreCount == 1 && v.AddressCount == interpolationEnd - interpolationStart))
				{
					return;
				}
			}
			else
			{
				return;
			}
			context.Step($"Transform DefaultInterpolatedStringHandler {v.Name}", stloc);
			var newVariable = v.Function.RegisterVariable(VariableKind.InitializerTarget, v.Type);
			var replacement = new Block(BlockKind.InterpolatedString);
			var initializer = stloc.Value;
			stloc.Value = replacement;
			replacement.Instructions.Add(new StLoc(newVariable, initializer));
			for (int i = interpolationStart + 1; i < interpolationEnd; i++)
			{
				var inst = block.Instructions[i];
				((LdLoca)((Call)inst).Arguments[0]).Variable = newVariable;
				replacement.Instructions.Add(inst);
			}
			var callToStringAndClear = insertionPoint;
			insertionPoint.ReplaceWith(replacement);
			((LdLoca)callToStringAndClear.Arguments[0]).Variable = newVariable;
			replacement.FinalInstruction = callToStringAndClear;
			block.Instructions.RemoveRange(interpolationStart, interpolationEnd - interpolationStart);
		}

		private bool IsKnownCall(Block block, int pos, ILVariable v)
		{
			if (pos >= block.Instructions.Count - 1)
				return false;
			if (!(block.Instructions[pos] is Call call))
				return false;
			if (!(call.Arguments.Count > 1))
				return false;
			if (!call.Arguments[0].MatchLdLoca(v))
				return false;
			if (call.Method.IsStatic)
				return false;
			if (!call.Method.DeclaringType.IsKnownType(KnownTypeCode.DefaultInterpolatedStringHandler))
				return false;
			switch (call.Method.Name)
			{
				case "AppendLiteral" when call.Arguments.Count == 2 && call.Arguments[1] is LdStr:
					break;
				case "AppendFormatted" when call.Arguments.Count <= 4:
					break;
				default:
					return false;
			}
			return true;
		}

		private bool FindToStringAndClear(Block block, int pos, ILVariable v, out Call insertionPoint)
		{
			insertionPoint = null;
			if (pos >= block.Instructions.Count)
				return false;
			// find
			// ... call ToStringAndClear(ldloca v) ...
			// in block.Instructions[pos]
			foreach (var use in v.AddressInstructions)
			{
				if (use.Parent is Call
					{
						Arguments: { Count: 1 },
						Method: { Name: "ToStringAndClear", IsStatic: false }
					} call
					&& call.Arguments[0].MatchLdLoca(v)
					&& call.IsDescendantOf(block.Instructions[pos]))
				{
					insertionPoint = call;
					return true;
				}
			}
			return false;
		}
	}
}

namespace System.Runtime.CompilerServices
{
	public class CallerArgumentExpressionAttribute : Attribute
	{
		public string ParameterName { get; }

		public CallerArgumentExpressionAttribute(string parameterName)
		{
			this.ParameterName = parameterName;
		}
	}
}