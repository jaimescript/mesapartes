using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

using MesaPartes.Models;

using System.Data;
using System.Data.Objects;
using System.Transactions;
using System.Web.Security;

using Rotativa;
using PagedList;

namespace MesaPartes.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        { 
            return View();
        }
/************************************ CERRAR SESION  ***********************************************/
        public ActionResult CerrarSesion()
        {
            FormsAuthentication.SignOut();

            /* evitar que se guarde en el historial del navegador y redirecciona al login*/
            Session.Abandon();
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Buffer = true;
            Response.ExpiresAbsolute = DateTime.Now.AddDays(-1d);
            Response.Expires = -1000;
            Response.CacheControl = "no-cache";
            /*...................................................*/

            return RedirectToAction("Login");
        }
/************************************ LOGIN FORMULARIO  ********************************************/
        public ActionResult Login()
        {
            return View();
        }
/************************************  LOGIN VALIDAR  **********************************************/
        [HttpPost]
        public ActionResult Login(Usuarios u, string returnUrl)
        {            
            if (ModelState.IsValid)
            {
                using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
                {
                    var v = db.Usuarios.Where(a => a.cUsuario.Equals(u.cUsuario) && a.cContraseña.Equals(u.cContraseña)).FirstOrDefault();
                    if (v != null)
                    {
                        FormsAuthentication.SetAuthCookie(u.cUsuario.ToString(), false);
                        if (Url.IsLocalUrl(returnUrl) && returnUrl.Length > 1 && returnUrl.StartsWith("/")
                            && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\"))
                        {
                            return RedirectToAction("Login");
                        }
                        else
                        {                           
                            return RedirectToAction("Formulario");
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("", "El usuario y/o password son incorrectos.");
                    }
                }
            }
            return View();
        }
/***************************************  FORMULARIO  **********************************************/
        [Authorize]
        public ActionResult Formulario()
        {
            return View();                                            
        }
/***************************************  FORMULARIO  VALIDAR  *************************************/
        [Authorize]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public ActionResult Formulario(MaestroBiblio alu)
        {
            if (ModelState.IsValid)
            {
                if (this.ValidarExisteCodigo(alu.codigo))
                {
                    if (!this.DeudaAlumnoAleph(alu.codigo))
                    {
                        return RedirectToAction("Buscar", new { CodigoAlumno = alu.codigo });
                    }
                    ModelState.AddModelError("codigo", "El usuario tiene una deuda de material bibliografico en la biblioteca.");
                }
                else
                {
                    ModelState.AddModelError("codigo", "El codigo de usuario no existe.");
                }                
            }
            return View();            
        }
/*********************************** BUSCAR FORMULARIO  ********************************************/
        [Authorize]        
        public ActionResult Buscar(string CodigoAlumno)
        {
            return View();      
        }
/********************************* BUSCAR VALIDAR FORMULARIO ***************************************/
        [Authorize]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public ActionResult Buscar(AuditoriaConstancias aud)
        {
            if (ModelState.IsValid)
            {
                if (this.ValidarExisteCodigo(aud.cCodLector))
                {
                    if (!this.DeudaAlumnoAleph(aud.cCodLector))
                    {
                        if (!this.ExpedienteDuplicado(aud.cIdExpediente))
                        {
                            if (this.Transaccion(aud.cCodLector, aud))
                            {
                                return RedirectToAction("Imprimir", aud);
                            }
                        }
                        ModelState.AddModelError("cIdExpediente", "Ya existe el expediente con este numero");                        
                    }
                    else
                    {
                        //ModelState.AddModelError("cCodLector", "El usuario tiene una deuda de material bibliografico en la biblioteca.");
                    }                    
                }
                else
                {
                    ModelState.AddModelError("cCodLector", "El codigo de usuario no existe.");
                }                
            }
            return View("Buscar");
        }
/****************************** TRANSACCION *********************************************************/
        public bool Transaccion(string codigo, AuditoriaConstancias aud)
        {
            // intentos de conexion, si se corta
            int intentos = 3;
            bool success = false;

            for (int i = 0; i < intentos; i++)
            {
                // Utiliza este bloque para asegurarse de la atomicidad del proceso 
                using (TransactionScope transaction = new TransactionScope())
                {
                    try
                    {
                         /* 
                         * Realiza las 3 operaciones
                         * 1.Actualiza (acceso_aleph, cybertesis, Estado) a X, del alumno
                         * 2.Agrega un nuevo expediente
                         * 3.Agrega el registro a la Tabla Auditoria
                         */

                        if (this.ActualizarEstadoAlumno(codigo) && this.AgregarExpediente(aud) && this.AgregarRegistroAuditoria(aud))
                        {                            
                            // Mark the transaction as complete.
                            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
                            {
                                db.AcceptAllChanges();
                                db.Dispose();
                            }
                            transaction.Complete();
                            success = true;
                            // si se conecta, sale del bucle for
                            break;
                        }
                        else
                        {
                            success = false;
                        }                       
                    }
                    catch (Exception ex)
                    {
                        // Handle errors and deadlocks here and retry if needed.
                        // Allow an UpdateException to pass through and 
                        // retry, otherwise stop the execution.
                        if (ex.GetType() != typeof(UpdateException))
                        {

                        }
                    }
                }              
            }
            if (success)
            {
                return success;
                // Reset the context since the operation succeeded.                
            }
            return success;
        }
/****************************** TRANSACCION ANULAR *********************************************************/
        public bool TransaccionAnular(string CodExpediente, string CodLector)
        {
            // intentos de conexion, si se corta
            int intentos = 3;
            bool success = false;

            for (int i = 0; i < intentos; i++)
            {
                // Utiliza este bloque para asegurarse de la atomicidad del proceso 
                using (TransactionScope transaction = new TransactionScope())
                {
                    try
                    {
                        /* 
                        * Realiza las 3 operaciones
                        * 1.Actualiza (acceso_aleph, cybertesis, Estado) a 'A', del alumno
                        * 2.Actualiza el expediente con campo anular = 'A'
                        * 3.Actualiza el registro a la Tabla Auditoria con anular = 'A'
                        */

                        if (this.ActualizarEstadoAlumnoAnular(CodLector) && this.AgregarExpedienteAnular(CodExpediente) && this.AgregarRegistroAuditoriaAnular(CodExpediente))
                        {
                            // Mark the transaction as complete.
                            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
                            {
                                db.AcceptAllChanges();
                                db.Dispose();
                            }
                            transaction.Complete();
                            success = true;
                            // si se conecta, sale del bucle for
                            break;
                        }
                        else
                        {
                            success = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle errors and deadlocks here and retry if needed.
                        // Allow an UpdateException to pass through and 
                        // retry, otherwise stop the execution.
                        if (ex.GetType() != typeof(UpdateException))
                        {

                        }
                    }
                }
            }
            if (success)
            {
                return success;
                // Reset the context since the operation succeeded.                
            }
            return success;
        }
/********************************* IMPRIMIR PDF ****************************************************/
        [Authorize]
        public ActionResult Imprimir(AuditoriaConstancias aud)
        {
            if (this.ValidarExisteCodigo(aud.cCodLector) && !this.DeudaAlumnoAleph(aud.cCodLector))           
            {
                using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
                {
                    var v = db.MaestroBiblio.Where(a => a.codigo.Equals(aud.cCodLector)).FirstOrDefault();
                    if (v != null)
                    {
                        //VARIABLES DEL ALUMNO                                                                    
                        ViewBag.datos = v.datos.ToString().Replace("/", " ");
                        ViewBag.codigo = v.codigo.ToString();

                        ViewBag.nrosalida = "34800";
                        ViewBag.escuela = v.programa.ToString();
                        ViewBag.alumno = v.datos.ToString();

                        // CADUCIDAD
                        ViewBag.caducidad = 45;

                        //NOMBRE COORDINADOR
                        ViewBag.coordinador = "Profesor Americo Herrea Vera";

                        // VARIABLES CONSTANCIA
                        ViewBag.nroexpediente = aud.cIdExpediente.ToString();
                        ViewBag.CodPapCons = aud.cCodPapelConstanc.ToString();
                        ViewBag.NroRec = aud.ReciboPapel.ToString();
                        ViewBag.NroRecSis = aud.ReciboSistema.ToString();

                        // FECHA Y HORA DEL SISTEMA
                        ViewBag.hora = DateTime.Now.TimeOfDay;
                        ViewBag.fecha = DateTime.Now.ToString("yyyy/MM/dd");

                        return new Rotativa.ViewAsPdf("Imprimir") { FileName = "Test.pdf"};
                        //return View();
                    }
                }
            }
            return View("Buscar");
        }
/********************************* LISTA EXPEDIENTES ****************************************************/
        [Authorize]
        public ActionResult ListarExpedientes(string sortOrder, string currentFilter, string searchString, int? page)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                ViewBag.CurrentSort = sortOrder;
                ViewBag.NameSortParm = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
                ViewBag.DateSortParm = sortOrder == "Date" ? "date_desc" : "Date";

                if (searchString != null)
                {
                    page = 1;
                }
                else
                {
                    searchString = currentFilter;
                }
                ViewBag.CurrentFilter = searchString;

                var Expedientes = from s in db.Expedientes select s;

                /*
                var Expedientes = from s in db.Expedientes                                  
                join sa in db.MaestroBiblio on s.CIdPrograma equals sa.cod_prog
                select new
                {
                    cCodLector = s.cCodLector,
                    cSalida = s.cSalida,
                    Fec_Expedición = s.Fec_Expedición,
                    CIdPrograma = s.CIdPrograma,
                    cDatos = s.cDatos,
                    cusuario = s.cusuario,
                    cIdExpediente = s.cIdExpediente,
                                      
                };* 
                 */
                /*var Expedientes = from s in db.Expedientes                                  
                                    join sa in db.MaestroBiblio on s.CIdPrograma equals sa.cod_prog
                                    select new
                                    {
                                        info = s,                                                                         
                                    };
                 */

                if (!String.IsNullOrEmpty(searchString))
                {
                    Expedientes = Expedientes.Where(s => s.cDatos.Contains(searchString.Replace(" ", "/")));
                }
                switch (sortOrder)
                {
                    case "name_desc":
                        Expedientes = Expedientes.OrderByDescending(s => s.cDatos);
                        break;
                    case "Date":
                        Expedientes = Expedientes.OrderBy(s => s.Fec_Expedición);
                        break;
                    case "date_desc":
                        Expedientes = Expedientes.OrderByDescending(s => s.Fec_Expedición);
                        break;
                    default:
                        Expedientes = Expedientes.OrderBy(s => s.cDatos);
                        break;
                }
                int pageSize = 15;
                int pageNumber = (page ?? 1);
                return View(Expedientes.ToPagedList(pageNumber, pageSize));                
            }            
        }
/****************************** ANULAR EXPEDIENTE *************************************************/
        [Authorize]        
        public ActionResult AnularExpediente(string cIdExpediente, string CodLector)
        {
            ViewBag.Anulado = "Expediente " + cIdExpediente + " con el codigo de alumno " + CodLector + " Anulado satisfactoriamente.";
            if (!this.TransaccionAnular(cIdExpediente, CodLector))
            {
                return RedirectToAction("ListarExpedientes");
            }
            return View();
        }
/*+++++++++++++++++++ FUNCIONES++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++*/
/******************************  VALIDAR EXISTENCIA CODIGO ALUMNO  *********************************/
        public bool ValidarExisteCodigo(string codigo)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                var alu = db.MaestroBiblio.Where(a => a.codigo.Equals(codigo)).FirstOrDefault();
                if (alu != null)
                {
                    return true;
                }
            }
            return false;
        }
/******************************  VALIDAR EXPEDIENTE DUPLICADO  *************************************/
        public bool ExpedienteDuplicado(string cIdExpediente)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                var e = db.Expedientes.Where(a => a.cIdExpediente.Equals(cIdExpediente)).FirstOrDefault();
                if (e != null)
                {
                    return true;
                }
            }
            return false;
        }
/******************************  CONSULTAR DEUDA ALUMNO  ********************************************/
        public bool DeudaAlumnoAleph(string codigo)
        {
            using (OracleEntities db = new OracleEntities())
            {
                var d = db.Z36.Where(a => a.Z36_ID.Trim().Equals(codigo)).FirstOrDefault();
                if (d != null)
                {
                    return true;
                }
            }
            return false;
        }
/*******************************  ACTUALIZAR ESTADO ALUMNO *****************************************/
        public bool ActualizarEstadoAlumno(string cod)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                MaestroBiblio alu = db.MaestroBiblio.Single(c => c.codigo == cod);

                alu.activo = "X";
                alu.cybertesis = "X";
                alu.Acceso_Aleph = "X";
                alu.Estado = "X";

                db.SaveChanges(SaveOptions.DetectChangesBeforeSave);
            }
            return true;
        }
/*******************************  ACTUALIZAR ESTADO ALUMNO ANULAR *****************************************/
        public bool ActualizarEstadoAlumnoAnular(string cod)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                MaestroBiblio alu = db.MaestroBiblio.Single(c => c.codigo == cod);

                alu.activo = "A";
                alu.cybertesis = "A";
                alu.Acceso_Aleph = "A";
                alu.Estado = "A";

                db.SaveChanges(SaveOptions.DetectChangesBeforeSave);
            }
            return true;
        }
/*********************************  AGREGAR EXPEDIENTE **********************************************/
        public bool AgregarExpediente(AuditoriaConstancias aud)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                Expedientes nuevo_e = new Expedientes();

                nuevo_e.cCodLector = aud.cCodLector;
                /*************  DATOS ALUMNO ********************************************************/
                MaestroBiblio alu = db.MaestroBiblio.Single(c => c.codigo == aud.cCodLector);
                nuevo_e.cDatos = alu.datos;
                nuevo_e.CIdPrograma = alu.cod_prog;
                /************************************************************************************/

                nuevo_e.cIdExpediente = aud.cIdExpediente;                
                nuevo_e.cSalida = 1500;
                nuevo_e.Fec_Expedición = DateTime.Now;
                db.Expedientes.AddObject(nuevo_e);
                db.SaveChanges();
            }
            return true;
        }
/*********************************  AGREGAR EXPEDIENTE **********************************************/
        public bool AgregarExpedienteAnular(string cIdExpediente)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                Expedientes exp = db.Expedientes.Single(e => e.cIdExpediente == cIdExpediente);                
                exp.anular = "A";
                db.SaveChanges(SaveOptions.DetectChangesBeforeSave);
            }
            return true;
        }
/**************************  AGREGAR REGISTRO EN TABLA AUDITORIA ************************************/
        public bool AgregarRegistroAuditoria(AuditoriaConstancias aud)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                AuditoriaConstancias nuevo_a = new AuditoriaConstancias();

                nuevo_a.cIdExpediente = aud.cIdExpediente;
                nuevo_a.cCodLector = aud.cCodLector;
                nuevo_a.cCodPapelConstanc = aud.cCodPapelConstanc;
                nuevo_a.ReciboPapel = aud.ReciboPapel;
                nuevo_a.ReciboSistema = aud.ReciboSistema;
                nuevo_a.Hora_Expediente = TimeSpan.Parse( "15:27:31");               
                //nuevo_a.Hora_Expediente = TimeSpan.Parse(DateTime.Now.ToString());
                nuevo_a.Fec_Expedición = DateTime.Now;
                nuevo_a.Control = "X";
                nuevo_a.Anular = "X";
                nuevo_a.Observacion = "nada";
                nuevo_a.cusuario = User.Identity.Name;

                db.AuditoriaConstancias.AddObject(nuevo_a);
                db.SaveChanges();
            }
            return true;
        }
/********************** AGREGAR REGSITRO AUDITORIA ANULAR *************************************/
        public bool AgregarRegistroAuditoriaAnular(string cIdExpediente)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                AuditoriaConstancias aud = db.AuditoriaConstancias.Single(c => c.cIdExpediente == cIdExpediente);
                aud.Anular = "A";                
                db.SaveChanges(SaveOptions.DetectChangesBeforeSave);
            }
            return true;
        }
/********************************* LISTA EXPEDIENTES *******************************************************/
        [Authorize]
        public ActionResult InfoAlumno(string CodigoAlumno, string RedirectUrl)
        {
            using (BIBLIO_UCSMEntities db = new BIBLIO_UCSMEntities())
            {
                ViewBag.direccion = "direccion: " + RedirectUrl;
                var v = db.MaestroBiblio.Where(a => a.codigo.Equals(CodigoAlumno)).FirstOrDefault();
                v.datos = v.datos.Replace("/", " ");
                return View(v);
            }
        }
/***********************************************************************************************************/
    }
}