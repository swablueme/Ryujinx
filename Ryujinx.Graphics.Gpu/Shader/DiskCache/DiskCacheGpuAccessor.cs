using Ryujinx.Common.Logging;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Image;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.Translation;
using System;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Gpu.Shader.DiskCache
{
    /// <summary>
    /// Represents a GPU state and memory accessor.
    /// </summary>
    class DiskCacheGpuAccessor : GpuAccessorBase, IGpuAccessor
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly ReadOnlyMemory<byte> _cb1Data;
        private readonly ShaderSpecializationState _oldSpecState;
        private readonly ShaderSpecializationState _newSpecState;
        private readonly int _stageIndex;
        private readonly bool _isVulkan;
        private readonly ResourceCounts _resourceCounts;

        /// <summary>
        /// Creates a new instance of the cached GPU state accessor for shader translation.
        /// </summary>
        /// <param name="context">GPU context</param>
        /// <param name="data">The data of the shader</param>
        /// <param name="cb1Data">The constant buffer 1 data of the shader</param>
        /// <param name="oldSpecState">Shader specialization state of the cached shader</param>
        /// <param name="newSpecState">Shader specialization state of the recompiled shader</param>
        /// <param name="stageIndex">Shader stage index</param>
        public DiskCacheGpuAccessor(
            GpuContext context,
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> cb1Data,
            ShaderSpecializationState oldSpecState,
            ShaderSpecializationState newSpecState,
            ResourceCounts counts,
            int stageIndex) : base(context, counts, stageIndex)
        {
            _data = data;
            _cb1Data = cb1Data;
            _oldSpecState = oldSpecState;
            _newSpecState = newSpecState;
            _stageIndex = stageIndex;
            _isVulkan = context.Capabilities.Api == TargetApi.Vulkan;
            _resourceCounts = counts;
        }

        /// <inheritdoc/>
        public uint ConstantBuffer1Read(int offset)
        {
            if (offset + sizeof(uint) > _cb1Data.Length)
            {
                throw new DiskCacheLoadException(DiskCacheLoadResult.InvalidCb1DataLength);
            }

            return MemoryMarshal.Cast<byte, uint>(_cb1Data.Span.Slice(offset))[0];
        }

        /// <inheritdoc/>
        public void Log(string message)
        {
            Logger.Warning?.Print(LogClass.Gpu, $"Shader translator: {message}");
        }

        /// <inheritdoc/>
        public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize)
        {
            return MemoryMarshal.Cast<byte, ulong>(_data.Span.Slice((int)address));
        }

        /// <inheritdoc/>
        public bool QueryAlphaToCoverageDitherEnable()
        {
            return _oldSpecState.GraphicsState.AlphaToCoverageEnable && _oldSpecState.GraphicsState.AlphaToCoverageDitherEnable;
        }

        /// <inheritdoc/>
        public AlphaTestOp QueryAlphaTestCompare()
        {
            if (!_isVulkan || !_oldSpecState.GraphicsState.AlphaTestEnable)
            {
                return AlphaTestOp.Always;
            }

            return _oldSpecState.GraphicsState.AlphaTestCompare switch
            {
                CompareOp.Never or CompareOp.NeverGl => AlphaTestOp.Never,
                CompareOp.Less or CompareOp.LessGl => AlphaTestOp.Less,
                CompareOp.Equal or CompareOp.EqualGl => AlphaTestOp.Equal,
                CompareOp.LessOrEqual or CompareOp.LessOrEqualGl => AlphaTestOp.LessOrEqual,
                CompareOp.Greater or CompareOp.GreaterGl => AlphaTestOp.Greater,
                CompareOp.NotEqual or CompareOp.NotEqualGl => AlphaTestOp.NotEqual,
                CompareOp.GreaterOrEqual or CompareOp.GreaterOrEqualGl => AlphaTestOp.GreaterOrEqual,
                _ => AlphaTestOp.Always
            };
        }

        /// <inheritdoc/>
        public float QueryAlphaTestReference() => _oldSpecState.GraphicsState.AlphaTestReference;

        /// <inheritdoc/>
        public AttributeType QueryAttributeType(int location)
        {
            return _oldSpecState.GraphicsState.AttributeTypes[location];
        }

        /// <inheritdoc/>
        public int QueryComputeLocalSizeX() => _oldSpecState.ComputeState.LocalSizeX;

        /// <inheritdoc/>
        public int QueryComputeLocalSizeY() => _oldSpecState.ComputeState.LocalSizeY;

        /// <inheritdoc/>
        public int QueryComputeLocalSizeZ() => _oldSpecState.ComputeState.LocalSizeZ;

        /// <inheritdoc/>
        public int QueryComputeLocalMemorySize() => _oldSpecState.ComputeState.LocalMemorySize;

        /// <inheritdoc/>
        public int QueryComputeSharedMemorySize() => _oldSpecState.ComputeState.SharedMemorySize;

        /// <inheritdoc/>
        public uint QueryConstantBufferUse()
        {
            _newSpecState.RecordConstantBufferUse(_stageIndex, _oldSpecState.ConstantBufferUse[_stageIndex]);
            return _oldSpecState.ConstantBufferUse[_stageIndex];
        }

        /// <inheritdoc/>
        public bool QueryHasConstantBufferDrawParameters()
        {
            return _oldSpecState.GraphicsState.HasConstantBufferDrawParameters;
        }

        /// <inheritdoc/>
        public InputTopology QueryPrimitiveTopology()
        {
            _newSpecState.RecordPrimitiveTopology();
            return ConvertToInputTopology(_oldSpecState.GraphicsState.Topology, _oldSpecState.GraphicsState.TessellationMode);
        }

        /// <inheritdoc/>
        public bool QueryProgramPointSize()
        {
            return _oldSpecState.GraphicsState.ProgramPointSizeEnable;
        }

        /// <inheritdoc/>
        public float QueryPointSize()
        {
            return _oldSpecState.GraphicsState.PointSize;
        }

        /// <inheritdoc/>
        public bool QueryTessCw()
        {
            return _oldSpecState.GraphicsState.TessellationMode.UnpackCw();
        }

        /// <inheritdoc/>
        public TessPatchType QueryTessPatchType()
        {
            return _oldSpecState.GraphicsState.TessellationMode.UnpackPatchType();
        }

        /// <inheritdoc/>
        public TessSpacing QueryTessSpacing()
        {
            return _oldSpecState.GraphicsState.TessellationMode.UnpackSpacing();
        }

        /// <inheritdoc/>
        public TextureFormat QueryTextureFormat(int handle, int cbufSlot)
        {
            _newSpecState.RecordTextureFormat(_stageIndex, handle, cbufSlot);
            (uint format, bool formatSrgb) = _oldSpecState.GetFormat(_stageIndex, handle, cbufSlot);
            return ConvertToTextureFormat(format, formatSrgb);
        }

        /// <inheritdoc/>
        public SamplerType QuerySamplerType(int handle, int cbufSlot)
        {
            _newSpecState.RecordTextureSamplerType(_stageIndex, handle, cbufSlot);
            return _oldSpecState.GetTextureTarget(_stageIndex, handle, cbufSlot).ConvertSamplerType();
        }

        /// <inheritdoc/>
        public bool QueryTextureCoordNormalized(int handle, int cbufSlot)
        {
            _newSpecState.RecordTextureCoordNormalized(_stageIndex, handle, cbufSlot);
            return _oldSpecState.GetCoordNormalized(_stageIndex, handle, cbufSlot);
        }

        /// <inheritdoc/>
        public bool QueryTransformDepthMinusOneToOne()
        {
            return _oldSpecState.GraphicsState.DepthMode;
        }

        /// <inheritdoc/>
        public bool QueryTransformFeedbackEnabled()
        {
            return _oldSpecState.TransformFeedbackDescriptors != null;
        }

        /// <inheritdoc/>
        public ReadOnlySpan<byte> QueryTransformFeedbackVaryingLocations(int bufferIndex)
        {
            return _oldSpecState.TransformFeedbackDescriptors[bufferIndex].AsSpan();
        }

        /// <inheritdoc/>
        public int QueryTransformFeedbackStride(int bufferIndex)
        {
            return _oldSpecState.TransformFeedbackDescriptors[bufferIndex].Stride;
        }

        /// <inheritdoc/>
        public bool QueryEarlyZForce()
        {
            _newSpecState.RecordEarlyZForce();
            return _oldSpecState.GraphicsState.EarlyZForce;
        }

        /// <inheritdoc/>
        public bool QueryViewportTransformDisable()
        {
            return _oldSpecState.GraphicsState.ViewportTransformDisable;
        }

        /// <inheritdoc/>
        public void RegisterTexture(int handle, int cbufSlot)
        {
            if (!_oldSpecState.TextureRegistered(_stageIndex, handle, cbufSlot))
            {
                throw new DiskCacheLoadException(DiskCacheLoadResult.MissingTextureDescriptor);
            }

            (uint format, bool formatSrgb) = _oldSpecState.GetFormat(_stageIndex, handle, cbufSlot);
            TextureTarget target = _oldSpecState.GetTextureTarget(_stageIndex, handle, cbufSlot);
            bool coordNormalized = _oldSpecState.GetCoordNormalized(_stageIndex, handle, cbufSlot);
            _newSpecState.RegisterTexture(_stageIndex, handle, cbufSlot, format, formatSrgb, target, coordNormalized);
        }
    }
}
