using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace RevitWindBrace
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        Result IExternalCommand.Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document revitDoc = commandData.Application.ActiveUIDocument.Document;  //取得文档           
            Application revitApp = commandData.Application.Application;             //取得应用程序            
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档        


            Window1 window1 = new Window1();
            List<XYZ> PPosition = new List<XYZ>();
            List<ElementId> selectedInstanceIDs = new List<ElementId>();
            Color colorOrigin = new Color(0, 0, 0);

            //窗口输入参数模块
            if (window1.ShowDialog() == true)
            {
                //窗口打开并停留，只有点击按键之后，窗口关闭并返回true
            }

            //按键会改变window的属性，通过对属性的循环判断来实现对按键的监测
            while (!window1.Done)
            {

                //选择一个腹杆
                if (window1.Selected)
                {
                    Selection sel = uiDoc.Selection;
                    Reference ref1 = sel.PickObject(ObjectType.Element, "选择一个缀板");
                    Element elem = revitDoc.GetElement(ref1);
                    FamilyInstance familyInstance = elem as FamilyInstance;

                    using (Transaction tran = new Transaction(uiDoc.Document))
                    {
                        tran.Start("选中腹板");

                        //初试颜色
                        Material materialOrigin = uiDoc.Document.GetElement(familyInstance.GetMaterialIds(false).First()) as Material;
                        colorOrigin = materialOrigin.Color;

                        //改变选中实例的线的颜色
                        OverrideGraphicSettings overrideGraphicSettings = new OverrideGraphicSettings();
                        overrideGraphicSettings = uiDoc.Document.ActiveView.GetElementOverrides(familyInstance.Id);
                        selectedInstanceIDs.Add(familyInstance.Id);

                        Color color = new Color(255, 0, 0);
                        overrideGraphicSettings.SetProjectionLineColor(color);
                        //在当前视图下设置，其它视图保持原来的
                        uiDoc.Document.ActiveView.SetElementOverrides(familyInstance.Id, overrideGraphicSettings);
                        uiDoc.Document.Regenerate();



                        //获取腹杆两端点坐标
                        IList<ElementId> ids = AdaptiveComponentInstanceUtils.GetInstancePointElementRefIds(familyInstance);
                        ReferencePoint referencePoint = uiDoc.Document.GetElement(ids[0]) as ReferencePoint;
                        PPosition.Add(referencePoint.Position);


                        referencePoint = uiDoc.Document.GetElement(ids[1]) as ReferencePoint;
                        PPosition.Add(referencePoint.Position);


                        tran.Commit();
                    }

                    window1.Selected = false;
                }


                if (window1.ShowDialog() == true)
                {
                    //窗口打开并停留，只有点击按键之后，窗口关闭并返回true
                }
            }

            //恢复轮廓线颜色
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("恢复轮廓线的颜色");

                //恢复标注前的颜色
                foreach (ElementId id in selectedInstanceIDs)
                {
                    OverrideGraphicSettings overrideGraphicSettings = new OverrideGraphicSettings();
                    overrideGraphicSettings = uiDoc.Document.ActiveView.GetElementOverrides(id);
                    overrideGraphicSettings.SetProjectionLineColor(colorOrigin);
                    //在当前视图下设置，其它视图保持原来的
                    uiDoc.Document.ActiveView.SetElementOverrides(id, overrideGraphicSettings);
                    uiDoc.Document.Regenerate();
                }

                tran.Commit();
            }



            //载入族
            FamilySymbol familySymbol;

            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("载入族");

                //载入弦杆族
                string file = @"C:\Users\zyx\Desktop\2RevitArcBridge\RevitWindBrace\RevitWindBrace\Source\WindBrace.rfa";
                familySymbol = loadFaimly(file, commandData);
                familySymbol.Activate();

                tran.Commit();
            }

            //风撑族实例化
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("创建风撑");

                createWindBrace(PPosition, familySymbol, commandData);

                tran.Commit();
            }

            return Result.Succeeded;
        }



        //载入族
        private FamilySymbol loadFaimly(string file, ExternalCommandData commandData)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档     
            bool loadSuccess = uiDoc.Document.LoadFamily(file, out Family family);

            if (loadSuccess)
            {
                //假如成功导入
                //得到族模板
                ElementId elementId;
                ISet<ElementId> symbols = family.GetFamilySymbolIds();
                elementId = symbols.First();
                FamilySymbol adaptiveFamilySymbol = uiDoc.Document.GetElement(elementId) as FamilySymbol;

                return adaptiveFamilySymbol;
            }
            else
            {
                //假如已经导入,则通过名字找到这个族
                FilteredElementCollector collector = new FilteredElementCollector(uiDoc.Document);
                collector.OfClass(typeof(Family));//过滤得到文档中所有的族
                IList<Element> families = collector.ToElements();
                FamilySymbol adaptiveFamilySymbol = null;
                foreach (Element e in families)
                {

                    Family f = e as Family;
                    //通过名字进行筛选
                    if (f.Name == "WindBrace")
                    {
                        adaptiveFamilySymbol = uiDoc.Document.GetElement(f.GetFamilySymbolIds().First()) as FamilySymbol;
                    }
                }
                return adaptiveFamilySymbol;

            }
        }


        //风箱族实例化
        private void createWindBrace(List<XYZ> points, FamilySymbol FamilySymbol, ExternalCommandData commandData)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档    

            //创建实例，并获取其自适应点列表
            FamilyInstance familyInstance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(uiDoc.Document, FamilySymbol);
            IList<ElementId> adaptivePoints = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(familyInstance);

            for (int i = 0; i < points.Count; i += 1)
            {
                //取得的参照点
                ReferencePoint referencePoint = uiDoc.Document.GetElement(adaptivePoints[i]) as ReferencePoint;
                //设置参照点坐标
                referencePoint.Position = points[i];
            }
        }
    }
}
