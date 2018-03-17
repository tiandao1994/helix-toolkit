﻿/*
The MIT License (MIT)
Copyright (c) 2018 Helix Toolkit contributors
*/

using SharpDX.Direct3D11;
using System.Collections.Generic;
using System.Linq;
#if NETFX_CORE
namespace HelixToolkit.UWP.Render
#else
namespace HelixToolkit.Wpf.SharpDX.Render
#endif
{
    using Core;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// 
    /// </summary>
    public class DeferredContextRenderer : ImmediateContextRenderer
    {
        private IDeviceContextPool deferredContextPool;
        private readonly IRenderTaskScheduler scheduler;
        private readonly List<KeyValuePair<int, CommandList>> commandList = new List<KeyValuePair<int, CommandList>>();
        private readonly CommandList[] postCommandList = new CommandList[2];
        private Task renderOthersTask;
        /// <summary>
        /// Initializes a new instance of the <see cref="DeferredContextRenderer"/> class.
        /// </summary>
        /// <param name="device">The device.</param>
        /// <param name="scheduler"></param>
        public DeferredContextRenderer(Device device, IRenderTaskScheduler scheduler) : base(device)
        {
            deferredContextPool = Collect(new DeviceContextPool(device));
            this.scheduler = scheduler;
        }

        /// <summary>
        /// Renders the scene.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="renderables">The renderables.</param>
        /// <param name="parameter">The parameter.</param>
        public override void RenderScene(IRenderContext context, List<IRenderCore> renderables, ref RenderParameter parameter)
        {          
            if (scheduler.ScheduleAndRun(renderables, deferredContextPool, context, parameter, RenderType.Opaque, commandList))
            {
                RenderParameter param = parameter;
                renderOthersTask = Task.Run(()=>
                {
                    RenderOthers(renderables, RenderType.Particle, context, deferredContextPool, ref param, postCommandList, 0);
                    RenderOthers(renderables, RenderType.Transparent, context, deferredContextPool, ref param, postCommandList, 1);
                });

                foreach(var command in commandList.OrderBy(x=>x.Key))
                {
                    ImmediateContext.DeviceContext.ExecuteCommandList(command.Value, false);
                    command.Value.Dispose();
                }

                commandList.Clear();
                renderOthersTask.Wait();
                renderOthersTask = null;
                for (int i = 0; i < postCommandList.Length; ++ i)
                {
                    ImmediateContext.DeviceContext.ExecuteCommandList(postCommandList[i], false);
                    postCommandList[i].Dispose();
                }
            }
            else
            {
                base.RenderScene(context, renderables, ref parameter);
            }
        }



        private void RenderOthers(List<IRenderCore> list, RenderType filter, IRenderContext context, IDeviceContextPool deviceContextPool,
            ref RenderParameter parameter,
            CommandList[] commandsArray,int idx)
        {
            var deviceContext = deviceContextPool.Get();
            SetRenderTargets(deviceContext, ref parameter);
            for(int i = 0; i < list.Count; ++i)
            {
                if(list[i].RenderType == filter)
                {
                    list[i].Render(context, deviceContext);
                }
            }
            commandsArray[idx] = deviceContext.DeviceContext.FinishCommandList(false);
            deviceContextPool.Put(deviceContext);
        }


        private void SetRenderTargets(DeviceContext context, ref RenderParameter parameter)
        {
            context.OutputMerger.SetTargets(parameter.DepthStencilView, parameter.RenderTargetView);
            context.Rasterizer.SetViewport(parameter.ViewportRegion);
            context.Rasterizer.SetScissorRectangle(parameter.ScissorRegion.Left, parameter.ScissorRegion.Top,
                parameter.ScissorRegion.Right, parameter.ScissorRegion.Bottom);
        }

        protected override void OnDispose(bool disposeManagedResources)
        {
            commandList.Clear();
            renderOthersTask?.Wait();
            base.OnDispose(disposeManagedResources);
        }
    }
}
