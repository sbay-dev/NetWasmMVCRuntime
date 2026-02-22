using WasmMvcRuntime.Abstractions.Mvc;

namespace WasmMvcRuntime.Abstractions
{
    /// <summary>
    /// A base class for an MVC controller with view support.
    /// </summary>
    public abstract class Controller : ControllerBase, IActionFilter, IAsyncActionFilter, IDisposable
    {
        private ITempDataDictionary? _tempData;
        private DynamicViewData? _viewBag;
        private ViewDataDictionary? _viewData;
        private ControllerContext _controllerContext = new();

        /// <summary>
        /// Gets or sets the ControllerContext.
        /// </summary>
        public ControllerContext ControllerContext
        {
            get => _controllerContext;
            set => _controllerContext = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets the HttpContext for the executing action.
        /// </summary>
        public WasmHttpContext? HttpContext => ControllerContext?.HttpContext;

        /// <summary>
        /// Gets or sets <see cref="ViewDataDictionary"/> used by <see cref="ViewResult"/> and <see cref="ViewBag"/>.
        /// </summary>
        [ViewDataDictionary]
        public ViewDataDictionary ViewData
        {
            get
            {
                if (_viewData == null)
                {
                    // This should run only for the controller unit test scenarios
                    _viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), ControllerContext.ModelState);
                }

                return _viewData!;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value), Resources.ArgumentCannotBeNullOrEmpty);
                }

                _viewData = value;
            }
        }

        /// <summary>
        /// Gets or sets <see cref="ITempDataDictionary"/> used by <see cref="ViewResult"/>.
        /// </summary>
        public ITempDataDictionary TempData
        {
            get
            {
                if (_tempData == null)
                {
                    var factory = HttpContext?.RequestServices?.GetRequiredService<ITempDataDictionaryFactory>();
                    _tempData = factory?.GetTempData(HttpContext!);
                }

                return _tempData!;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                _tempData = value;
            }
        }

        /// <summary>
        /// Gets the dynamic view bag.
        /// </summary>
        public dynamic ViewBag
        {
            get
            {
                if (_viewBag == null)
                {
                    _viewBag = new DynamicViewData(() => ViewData);
                }

                return _viewBag;
            }
        }

        /// <summary>
        /// Creates a <see cref="ViewResult"/> object that renders a view to the response.
        /// </summary>
        [NonAction]
        public virtual ViewResult View()
        {
            return View(viewName: null);
        }

        /// <summary>
        /// Creates a <see cref="ViewResult"/> object by specifying a <paramref name="viewName"/>.
        /// </summary>
        [NonAction]
        public virtual ViewResult View(string? viewName)
        {
            return View(viewName, model: ViewData.Model);
        }

        /// <summary>
        /// Creates a <see cref="ViewResult"/> object by specifying a <paramref name="model"/>
        /// to be rendered by the view.
        /// </summary>
        [NonAction]
        public virtual ViewResult View(object? model)
        {
            return View(viewName: null, model: model);
        }

        /// <summary>
        /// Creates a <see cref="ViewResult"/> object by specifying a <paramref name="viewName"/>
        /// and the <paramref name="model"/> to be rendered by the view.
        /// </summary>
        [NonAction]
        public virtual ViewResult View(string? viewName, object? model)
        {
            ViewData.Model = model;

            // Extract controller name from the class name
            var controllerName = GetType().Name.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);

            return new ViewResult()
            {
                ViewName = viewName,
                ViewData = ViewData,
                TempData = TempData,
                ControllerName = controllerName
            };
        }

        /// <summary>
        /// Creates a <see cref="PartialViewResult"/> object that renders a partial view to the response.
        /// </summary>
        [NonAction]
        public virtual PartialViewResult PartialView()
        {
            return PartialView(viewName: null);
        }

        /// <summary>
        /// Creates a <see cref="PartialViewResult"/> object by specifying a <paramref name="viewName"/>.
        /// </summary>
        [NonAction]
        public virtual PartialViewResult PartialView(string? viewName)
        {
            return PartialView(viewName, model: ViewData.Model);
        }

        /// <summary>
        /// Creates a <see cref="PartialViewResult"/> object by specifying a <paramref name="model"/>
        /// to be rendered by the partial view.
        /// </summary>
        [NonAction]
        public virtual PartialViewResult PartialView(object? model)
        {
            return PartialView(viewName: null, model: model);
        }

        /// <summary>
        /// Creates a <see cref="PartialViewResult"/> object by specifying a <paramref name="viewName"/>
        /// and the <paramref name="model"/> to be rendered by the partial view.
        /// </summary>
        [NonAction]
        public virtual PartialViewResult PartialView(string? viewName, object? model)
        {
            ViewData.Model = model;

            var controllerName = GetType().Name.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);

            return new PartialViewResult()
            {
                ViewName = viewName,
                ViewData = ViewData,
                TempData = TempData,
                ControllerName = controllerName
            };
        }

        /// <summary>
        /// Creates a <see cref="ViewComponentResult"/> by specifying the name of a view component to render.
        /// </summary>
        [NonAction]
        public virtual ViewComponentResult ViewComponent(string componentName)
        {
            return ViewComponent(componentName, arguments: null);
        }

        /// <summary>
        /// Creates a <see cref="ViewComponentResult"/> by specifying the <see cref="Type"/> of a view component to
        /// render.
        /// </summary>
        [NonAction]
        public virtual ViewComponentResult ViewComponent(Type componentType)
        {
            return ViewComponent(componentType, arguments: null);
        }

        /// <summary>
        /// Creates a <see cref="ViewComponentResult"/> by specifying the name of a view component to render.
        /// </summary>
        [NonAction]
        public virtual ViewComponentResult ViewComponent(string componentName, object? arguments)
        {
            return new ViewComponentResult
            {
                ViewComponentName = componentName,
                Arguments = arguments,
                ViewData = ViewData,
                TempData = TempData
            };
        }

        /// <summary>
        /// Creates a <see cref="ViewComponentResult"/> by specifying the <see cref="Type"/> of a view component to
        /// render.
        /// </summary>
        [NonAction]
        public virtual ViewComponentResult ViewComponent(Type componentType, object? arguments)
        {
            return new ViewComponentResult
            {
                ViewComponentType = componentType,
                Arguments = arguments,
                ViewData = ViewData,
                TempData = TempData
            };
        }

        /// <summary>
        /// Creates a <see cref="JsonResult"/> object that serializes the specified <paramref name="data"/> object
        /// to JSON.
        /// </summary>
        [NonAction]
        public new virtual JsonResult Json(object? data)
        {
            return new JsonResult(data);
        }

        /// <summary>
        /// Creates a <see cref="JsonResult"/> object that serializes the specified <paramref name="data"/> object
        /// to JSON.
        /// </summary>
        /// <param name="data">The object to serialize.</param>
        /// <param name="serializerSettings">The serializer settings (not used in this implementation).</param>
        [NonAction]
        public virtual JsonResult Json(object? data, object? serializerSettings)
        {
            // For now, we ignore serializerSettings as JsonResult doesn't support it
            return new JsonResult(data);
        }

        /// <summary>
        /// Called before the action method is invoked.
        /// </summary>
        [NonAction]
        public virtual void OnActionExecuting(ActionExecutingContext context)
        {
        }

        /// <summary>
        /// Called after the action method is invoked.
        /// </summary>
        [NonAction]
        public virtual void OnActionExecuted(ActionExecutedContext context)
        {
        }

        /// <summary>
        /// Called before the action method is invoked.
        /// </summary>
        [NonAction]
        public virtual Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            OnActionExecuting(context);
            if (context.Result == null)
            {
                var task = next();
                if (!task.IsCompletedSuccessfully)
                {
                    return Awaited(this, task);
                }

                OnActionExecuted(task.Result);
            }

            return Task.CompletedTask;

            static async Task Awaited(Controller controller, Task<ActionExecutedContext> task)
            {
                controller.OnActionExecuted(await task);
            }
        }

        /// <inheritdoc />
        public void Dispose() => Dispose(disposing: true);

        /// <summary>
        /// Releases all resources currently used by this <see cref="Controller"/> instance.
        /// </summary>
        /// <param name="disposing"><c>true</c> if this method is being invoked by the <see cref="Dispose()"/> method,
        /// otherwise <c>false</c>.</param>
        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
