using VIIDII.Models;
using VIIDII.Data;
using VIIDII.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace VIIDII.ViewComponents
{
    /// <summary>
    /// Reads the signed in person straight from the database.
    /// <para>
    /// This used to resolve them from <c>MockApiService</c>, a hardcoded list of demo
    /// users, which meant the navigation bar only ever recognised those names. It also
    /// read the matric number out of session state, so it disagreed with the
    /// authentication cookie the rest of the app uses.
    /// </para>
    /// </summary>
    public class NavBarViewComponent : ViewComponent
    {
        public async Task<IViewComponentResult> InvokeAsync()
        {
            // CurrentUserAccessor reads the authentication cookie, which is what the
            // rest of the app uses. Reading session state here meant this component
            // could disagree with the page around it.
            var currentUserAccessor =
                HttpContext.RequestServices.GetRequiredService<CurrentUserAccessor>();

            var matricNo = currentUserAccessor.MatricNo;

            var currentUser = string.IsNullOrEmpty(matricNo)
                ? null
                : await HttpContext.RequestServices
                    .GetRequiredService<ViidiiDbContext>()
                    .Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.MatricNo == matricNo);

            var model = new NavBarViewModel
            {
                MatricNo = matricNo,
                IsLecturer = currentUser?.Role == Role.Lecturer,
                IsAdmin = currentUser?.Role == Role.Admin
            };

            return View(model);
        }
    }

    public class NavBarViewModel
    {
        public string? MatricNo { get; set; }
        public bool IsLecturer { get; set; }
        public bool IsAdmin { get; set; }
    }
}