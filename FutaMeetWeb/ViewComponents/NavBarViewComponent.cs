using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FutaMeetWeb.ViewComponents
{
    public class NavBarViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var matricNo = HttpContext.Session.GetString("MatricNo");
            var currentUser = MockApiService.GetUsers().Where(u => u.MatricNo == matricNo).FirstOrDefault();
            var admins = MockApiService.GetUsers().Where(u => u.Role == Role.Admin).ToList();
            var model = new NavBarViewModel
            {
                MatricNo = matricNo,
                IsLecturer = !string.IsNullOrEmpty(matricNo) &&
                 MockApiService.GetLecturers().Any(l => l.MatricNo == matricNo),
                IsAdmin = admins.Contains(currentUser)
            };
            return View(model);
        }
    }
    public class NavBarViewModel
    {
        public string MatricNo { get; set; }
        public bool IsLecturer { get; set; }
        public bool IsAdmin { get; set; }
    }
}
